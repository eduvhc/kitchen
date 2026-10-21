using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ResultingKit;

namespace StoringKit.S3;

/// <summary><see cref="IObjectStore"/> over one bucket of an S3-compatible store.</summary>
internal sealed class S3ObjectStore(IAmazonS3 client, S3StoreOptions options, TimeProvider timeProvider)
    : ObjectStore(options.PublicBaseUrl, timeProvider), IDisposable
{
    /// <summary>
    /// Where the MD5 is kept: S3 verifies <c>Content-MD5</c> on upload but does not return it. The hyphen keeps it
    /// apart from user metadata, whose names cannot contain one.
    /// </summary>
    internal const string Md5MetadataName = "storingkit-md5";

    private const string MetadataPrefix = "x-amz-meta-";

    private readonly IAmazonS3 _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly string _bucket = string.IsNullOrWhiteSpace(options.BucketName)
        ? throw new ArgumentException("A bucket name is required.", nameof(options))
        : options.BucketName;
    private readonly bool _isAws = options.IsAwsEndpoint;
    private readonly bool _https = options.ServiceUrl is null || options.ServiceUrl.Scheme == Uri.UriSchemeHttps;

    public void Dispose() => _client.Dispose();

    internal static AmazonS3Config CreateConfig(S3StoreOptions options)
    {
        var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };

        if (options.ServiceUrl is { } serviceUrl)
        {
            config.ServiceURL = serviceUrl.AbsoluteUri;
            config.AuthenticationRegion = options.Region ?? "us-east-1";
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        if (!options.IsAwsEndpoint)
        {
            // AWS SDKs released since December 2024 default to WHEN_SUPPORTED, adding CRC checksum headers and
            // trailers that R2 and other S3-compatible stores reject.
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }

        return config;
    }

    internal static AmazonS3Client CreateClient(S3StoreOptions options)
    {
        var config = CreateConfig(options);
        var credentials = options.Credentials
            ?? (options.AccessKeyId is { Length: > 0 } id ? new BasicAWSCredentials(id, options.SecretAccessKey) : null);

        return credentials is null ? new AmazonS3Client(config) : new AmazonS3Client(credentials, config);
    }

    protected override async Task<PutReceipt> PutCoreAsync(ObjectWrite write, CancellationToken ct)
    {
        // PutObject needs the length up front and rewinds to position 0 on retry, so anything else is buffered.
        var buffered = write.Length is null || write.Content.Position != 0
            ? await BufferAsync(write.Content, ct).ConfigureAwait(false)
            : null;

        try
        {
            var body = buffered?.Stream ?? write.Content;
            var md5 = write.ContentMd5 ?? buffered?.Md5;

            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = write.Key,
                InputStream = body,
                AutoCloseStream = false,
                ContentType = write.ContentType,
                MD5Digest = md5,
            };

            request.Headers.ContentLength = body.Length;

            if (!_isAws)
            {
                // R2 does not implement streaming SigV4 (aws-chunked); over HTTPS the payload goes unsigned instead.
                request.UseChunkEncoding = false;

                if (_https)
                {
                    request.DisablePayloadSigning = true;
                }
            }

            foreach (var (name, value) in write.Metadata)
            {
                request.Metadata.Add(name, value);
            }

            if (md5 is not null)
            {
                request.Metadata.Add(Md5MetadataName, md5);
            }

            if (write.Condition is { } condition)
            {
                if (condition.IsCreateOnly)
                {
                    request.IfNoneMatch = "*";
                }
                else
                {
                    request.IfMatch = condition.IfMatchETag;
                }
            }

            var response = await _client.PutObjectAsync(request, ct).ConfigureAwait(false);

            return new PutReceipt
            {
                Key = write.Key,
                ETag = response.ETag,
                Size = body.Length,
                ContentMd5 = md5,
            };
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    protected override async Task<ObjectContent> GetCoreAsync(string key, CancellationToken ct)
    {
        var response = await _client
            .GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = key }, ct)
            .ConfigureAwait(false);

        var (metadata, md5) = ReadMetadata(response.Metadata);

        var properties = new StoredObject
        {
            Key = key,
            Size = response.ContentLength,
            ContentType = response.Headers.ContentType ?? DefaultContentType,
            ETag = response.ETag,
            LastModified = ToUtc(response.LastModified),
            Metadata = metadata,
            ContentMd5 = md5,
        };

        return new ObjectContent(properties, response.ResponseStream);
    }

    protected override async Task<StoredObject> GetPropertiesCoreAsync(string key, CancellationToken ct)
    {
        var response = await _client
            .GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, ct)
            .ConfigureAwait(false);

        var (metadata, md5) = ReadMetadata(response.Metadata);

        return new StoredObject
        {
            Key = key,
            Size = response.ContentLength,
            ContentType = response.Headers.ContentType ?? DefaultContentType,
            ETag = response.ETag,
            LastModified = ToUtc(response.LastModified),
            Metadata = metadata,
            ContentMd5 = md5,
        };
    }

    protected override async Task<bool> ExistsCoreAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client
                .GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, ct)
                .ConfigureAwait(false);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorCode != "NoSuchBucket")
        {
            return false;
        }
    }

    protected override Task DeleteCoreAsync(string key, CancellationToken ct) =>
        _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }, ct);

    protected override async Task<ObjectPage> ListCoreAsync(string? prefix, int pageSize, string? continuationToken, CancellationToken ct)
    {
        var response = await _client
            .ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix,
                    MaxKeys = pageSize,
                    ContinuationToken = continuationToken,
                },
                ct)
            .ConfigureAwait(false);

        var objects = (response.S3Objects ?? [])
            .Select(o => new ObjectSummary(o.Key, o.Size ?? 0, o.ETag, ToUtc(o.LastModified)))
            .ToList();

        var next = response.IsTruncated == true && !string.IsNullOrEmpty(response.NextContinuationToken)
            ? response.NextContinuationToken
            : null;

        return new ObjectPage(objects, next);
    }

    protected override async Task<Uri> GetReadUrlCoreAsync(string key, DateTimeOffset expiresOn, CancellationToken ct)
    {
        var url = await _client
            .GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = expiresOn.UtcDateTime,
                Protocol = _https ? Protocol.HTTPS : Protocol.HTTP,
            })
            .ConfigureAwait(false);

        return new Uri(url);
    }

    protected override Error? TranslateException(Exception exception, ObjectStoreOperation operation, string key, WriteCondition? condition) =>
        exception switch
        {
            AmazonServiceException service when service.StatusCode != 0 => Translate(service, operation, key, condition),
            AmazonClientException { InnerException: { } inner } => TranslateTransportException(inner),
            _ => null,
        };

    internal static Error? Translate(AmazonServiceException exception, ObjectStoreOperation operation, string key, WriteCondition? condition)
    {
        var code = exception.ErrorCode;
        var status = (int)exception.StatusCode;
        var isPut = operation == ObjectStoreOperation.Put;

        return (status, code) switch
        {
            (_, "NoSuchBucket") => null,

            (404, _) when isPut && condition is not null => StoringErrors.ConditionNotMet(key, condition),
            (404, _) => StoringErrors.MissingObject(key),

            (412, _) when isPut => StoringErrors.ConditionNotMet(key, condition),

            // A conditional write raced another one on the same key; the other won.
            (409, "ConditionalRequestConflict") when isPut => StoringErrors.ConditionNotMet(key, condition),

            (_, "BadDigest" or "InvalidDigest") => StoringErrors.CorruptUpload(key),
            (_, "EntityTooLarge") => StoringErrors.ContentTooLarge(key),

            (_, "SlowDown" or "Throttling" or "ThrottlingException" or "TooManyRequests" or "RequestLimitExceeded") =>
                StoringErrors.Throttling(code),
            (429, _) => StoringErrors.Throttling(code ?? "429 Too Many Requests"),

            (_, "RequestTimeout") => StoringErrors.TimedOut(code),
            (408, _) => StoringErrors.TimedOut(code ?? "408 Request Timeout"),

            // R2 and other S3-compatible stores answer 501 for features they lack; retrying will not add them.
            (501, _) => null,
            ( >= 500, _) => StoringErrors.ServiceUnavailable(code ?? $"{status}"),

            _ => null,
        };
    }

    private static (IReadOnlyDictionary<string, string> Metadata, string? Md5) ReadMetadata(MetadataCollection collection)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        string? md5 = null;

        foreach (var rawName in collection.Keys)
        {
            var value = collection[rawName];
            var name = rawName.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase)
                ? rawName[MetadataPrefix.Length..]
                : rawName;

            name = name.ToLowerInvariant();

            if (name == Md5MetadataName)
            {
                md5 = value;
            }
            else
            {
                metadata[name] = value;
            }
        }

        return (metadata, md5);
    }

    private static DateTimeOffset ToUtc(DateTime? value) => value switch
    {
        null => DateTimeOffset.MinValue,
        { Kind: DateTimeKind.Unspecified } v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)),
        { } v => new DateTimeOffset(v.ToUniversalTime()),
    };

    [SuppressMessage("Security", "CA5351:Do not use broken cryptographic algorithms",
        Justification = "MD5 is the Content-MD5 integrity checksum S3 verifies; it guards against corruption, not tampering.")]
    private static async Task<BufferedContent> BufferAsync(Stream source, CancellationToken ct)
    {
        var path = Path.GetTempFileName();
        var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        try
        {
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            var buffer = new byte[81920];
            int read;

            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                md5.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            await file.FlushAsync(ct).ConfigureAwait(false);
            file.Position = 0;

            return new BufferedContent(file, Convert.ToBase64String(md5.GetHashAndReset()));
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed record BufferedContent(FileStream Stream, string Md5) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }
}
