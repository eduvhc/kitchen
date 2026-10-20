using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ResultingKit;

namespace StoringKit.Azure;

/// <summary><see cref="IObjectStore"/> over one Azure Blob Storage container, as block blobs.</summary>
internal sealed class AzureBlobObjectStore : ObjectStore, IDisposable
{
    // User delegation keys live at most seven days; refresh a little before the end so signing never races expiry.
    private static readonly TimeSpan DelegationKeyLifetime = TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5);

    private readonly BlobContainerClient _container;
    private readonly BlobServiceClient _service;
    private readonly SemaphoreSlim _delegationKeyLock = new(1, 1);
    private readonly bool _canUseDelegationKey;
    private UserDelegationKey? _delegationKey;

    /// <param name="service">The blob service client.</param>
    /// <param name="containerName">The container every key lives in.</param>
    /// <param name="canUseDelegationKey">Whether <paramref name="service"/> holds a token credential that can request user delegation keys.</param>
    /// <param name="publicBaseUrl">The public URL of the container, if it is public.</param>
    /// <param name="timeProvider">The clock read URLs expire against.</param>
    public AzureBlobObjectStore(
        BlobServiceClient service,
        string containerName,
        bool canUseDelegationKey,
        Uri? publicBaseUrl,
        TimeProvider timeProvider)
        : base(publicBaseUrl, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        _service = service;
        _canUseDelegationKey = canUseDelegationKey;
        _container = service.GetBlobContainerClient(containerName);
    }

    public void Dispose() => _delegationKeyLock.Dispose();

    protected override async Task<PutReceipt> PutCoreAsync(ObjectWrite write, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(write.Key);
        var counting = write.Length is null ? new CountingReadStream(write.Content) : null;

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = write.ContentType,
                ContentHash = write.ContentMd5 is null ? null : Convert.FromBase64String(write.ContentMd5),
            },
            Metadata = write.Metadata.ToDictionary(StringComparer.Ordinal),
            Conditions = ToConditions(write.Condition),

            // The SDK sends an MD5 with every request it makes — the whole blob, or each staged block — and the service
            // rejects any that arrive corrupted. ContentHash above is the whole-object MD5 stored on the blob.
            TransferValidation = new UploadTransferValidationOptions { ChecksumAlgorithm = StorageChecksumAlgorithm.MD5 },
        };

        var response = await blob.UploadAsync(counting ?? write.Content, options, ct).ConfigureAwait(false);

        return new PutReceipt
        {
            Key = write.Key,
            ETag = response.Value.ETag.ToString("H"),
            Size = write.Length ?? counting!.BytesRead,
            ContentMd5 = write.ContentMd5,
        };
    }

    protected override async Task<ObjectContent> GetCoreAsync(string key, CancellationToken ct)
    {
        var response = await _container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
        var details = response.Value.Details;

        var properties = new StoredObject
        {
            Key = key,
            Size = details.ContentLength,
            ContentType = details.ContentType ?? DefaultContentType,
            ETag = details.ETag.ToString("H"),
            LastModified = details.LastModified,
            Metadata = ToMetadata(details.Metadata),
            ContentMd5 = ToBase64(details.ContentHash),
        };

        return new ObjectContent(properties, response.Value.Content);
    }

    protected override async Task<StoredObject> GetPropertiesCoreAsync(string key, CancellationToken ct)
    {
        var response = await _container.GetBlobClient(key).GetPropertiesAsync(cancellationToken: ct).ConfigureAwait(false);
        var properties = response.Value;

        return new StoredObject
        {
            Key = key,
            Size = properties.ContentLength,
            ContentType = properties.ContentType ?? DefaultContentType,
            ETag = properties.ETag.ToString("H"),
            LastModified = properties.LastModified,
            Metadata = ToMetadata(properties.Metadata),
            ContentMd5 = ToBase64(properties.ContentHash),
        };
    }

    protected override async Task<bool> ExistsCoreAsync(string key, CancellationToken ct) =>
        (await _container.GetBlobClient(key).ExistsAsync(ct).ConfigureAwait(false)).Value;

    protected override Task DeleteCoreAsync(string key, CancellationToken ct) =>
        _container.GetBlobClient(key).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);

    protected override async Task<ObjectPage> ListCoreAsync(string? prefix, int pageSize, string? continuationToken, CancellationToken ct)
    {
        var pages = _container
            .GetBlobsAsync(traits: BlobTraits.None, states: BlobStates.None, prefix: prefix, cancellationToken: ct)
            .AsPages(continuationToken, pageSize);

        await foreach (var page in pages.ConfigureAwait(false))
        {
            var objects = page.Values
                .Select(item => new ObjectSummary(
                    item.Name,
                    item.Properties.ContentLength ?? 0,
                    item.Properties.ETag?.ToString("H") ?? string.Empty,
                    item.Properties.LastModified ?? DateTimeOffset.MinValue))
                .ToList();

            return new ObjectPage(objects, string.IsNullOrEmpty(page.ContinuationToken) ? null : page.ContinuationToken);
        }

        return new ObjectPage([], null);
    }

    protected override async Task<Uri> GetReadUrlCoreAsync(string key, DateTimeOffset expiresOn, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(key);

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(BlobSasPermissions.Read, expiresOn);
        }

        if (!_canUseDelegationKey)
        {
            throw new InvalidOperationException(
                "This Azure store has neither an account key nor a Microsoft Entra credential, so it cannot sign read URLs.");
        }

        var delegationKey = await GetDelegationKeyAsync(expiresOn, ct).ConfigureAwait(false);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
        {
            BlobContainerName = _container.Name,
            BlobName = key,
            Resource = "b",
        };

        return new BlobUriBuilder(blob.Uri)
        {
            Sas = sas.ToSasQueryParameters(delegationKey, _container.AccountName),
        }.ToUri();
    }

    protected override Error? TranslateException(Exception exception, ObjectStoreOperation operation, string key, WriteCondition? condition) =>
        exception is RequestFailedException failed ? Translate(failed, operation, key, condition) : null;

    internal static Error? Translate(RequestFailedException exception, ObjectStoreOperation operation, string key, WriteCondition? condition)
    {
        var code = exception.ErrorCode;

        return exception.Status switch
        {
            // No response at all: the transport failed, and the inner exception says how.
            0 => exception.InnerException is null ? null : TranslateTransportException(exception.InnerException),

            404 when code is "ContainerNotFound" => null,
            404 when operation == ObjectStoreOperation.Put && condition is not null => StoringErrors.ConditionNotMet(key, condition),
            404 => StoringErrors.MissingObject(key),

            409 when code is "BlobAlreadyExists" && operation == ObjectStoreOperation.Put => StoringErrors.ConditionNotMet(key, condition),
            412 when operation == ObjectStoreOperation.Put => StoringErrors.ConditionNotMet(key, condition),

            400 when code is "Md5Mismatch" or "InvalidMd5" or "Crc64Mismatch" => StoringErrors.CorruptUpload(key),

            // Azurite reports a mismatch as a generic InvalidOperation.
            400 when code is "InvalidOperation" && exception.Message.Contains("MD5", StringComparison.OrdinalIgnoreCase) =>
                StoringErrors.CorruptUpload(key),
            400 or 413 when code is "RequestBodyTooLarge" => StoringErrors.ContentTooLarge(key),

            408 => StoringErrors.TimedOut(code ?? "408 Request Timeout"),
            500 when code is "OperationTimedOut" => StoringErrors.TimedOut(code),
            429 => StoringErrors.Throttling(code ?? "429 Too Many Requests"),
            503 when code is "ServerBusy" => StoringErrors.Throttling(code),
            >= 500 => StoringErrors.ServiceUnavailable(code ?? $"{exception.Status}"),

            _ => null,
        };
    }

    private static BlobRequestConditions? ToConditions(WriteCondition? condition) => condition switch
    {
        null => null,
        { IsCreateOnly: true } => new BlobRequestConditions { IfNoneMatch = ETag.All },
        _ => new BlobRequestConditions { IfMatch = new ETag(condition.IfMatchETag!) },
    };

    private static Dictionary<string, string> ToMetadata(IDictionary<string, string>? metadata) =>
        metadata is null
            ? []
            : metadata.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value, StringComparer.Ordinal);

    private static string? ToBase64(byte[]? hash) => hash is { Length: > 0 } ? Convert.ToBase64String(hash) : null;

    private async Task<UserDelegationKey> GetDelegationKeyAsync(DateTimeOffset needsToLast, CancellationToken ct)
    {
        await _delegationKeyLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_delegationKey is { } cached && cached.SignedExpiresOn >= needsToLast)
            {
                return cached;
            }

            var now = TimeProvider.GetUtcNow();
            var response = await _service
                .GetUserDelegationKeyAsync(now.AddMinutes(-5), now + DelegationKeyLifetime, ct)
                .ConfigureAwait(false);

            _delegationKey = response.Value;
            return response.Value;
        }
        finally
        {
            _delegationKeyLock.Release();
        }
    }
}
