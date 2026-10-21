using System.Globalization;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using ResultingKit;

namespace LockingKit.Azure;

/// <summary>Locks as leases on guard blobs in one Azure Blob Storage container.</summary>
internal sealed class AzureBlobLeaseBackend(BlobContainerClient container, string prefix, TimeSpan leaseDuration, bool fencingTokens)
    : ILockBackend
{
    /// <summary>The metadata entry that carries the last fencing token issued for a guard.</summary>
    internal const string FenceMetadataName = "lockingkit_fence";

    private readonly BlobContainerClient _container = container ?? throw new ArgumentNullException(nameof(container));
    private readonly string _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));

    public LeaseTiming Timing { get; } = LeaseTiming.Expiring(leaseDuration);

    public async Task<ILockLease?> TryAcquireAsync(string key, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(BlobLockNames.ToBlobName(_prefix, key));
        var lease = blob.GetBlobLeaseClient(Guid.NewGuid().ToString());

        if (!await TryLeaseAsync(blob, lease, ct).ConfigureAwait(false))
        {
            return null;
        }

        var held = new HeldBlobLease(blob, lease);

        if (!fencingTokens)
        {
            return held;
        }

        try
        {
            held.FencingToken = await IssueFencingTokenAsync(blob, lease.LeaseId, ct).ConfigureAwait(false);
            return held;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            // The lease was lost or broken between acquiring and fencing: someone else holds the key now.
            return null;
        }
        catch
        {
            await held.ReleaseQuietlyAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Error? TranslateException(Exception exception) =>
        exception is RequestFailedException failed ? Translate(failed) : null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static Error? Translate(RequestFailedException exception) => exception.Status switch
    {
        // No response at all: the transport failed, and the inner exception says how.
        0 => exception.InnerException is null
            ? LockingErrors.BackendUnavailable(exception.Message)
            : DistributedLock.TranslateTransportException(exception.InnerException) ?? LockingErrors.BackendUnavailable(exception.Message),
        408 or 429 or >= 500 => LockingErrors.BackendUnavailable(exception.ErrorCode ?? $"{exception.Status}"),

        // Wrong credentials, a denied permission, a missing container: no retry fixes these.
        401 or 403 => LockingErrors.Misconfiguration(exception.ErrorCode ?? $"{exception.Status}"),
        404 when exception.ErrorCode is "ContainerNotFound" => LockingErrors.Misconfiguration("ContainerNotFound"),

        // Anything else is reported by the engine as locking.backend_failed.
        _ => null,
    };

    internal static bool IsLeaseHeldElsewhere(RequestFailedException exception) =>
        exception.Status == 409 && exception.ErrorCode is "LeaseAlreadyPresent" or "LeaseIsBreakingAndCannotBeAcquired";

    private async Task<bool> TryLeaseAsync(BlobClient blob, BlobLeaseClient lease, CancellationToken ct)
    {
        try
        {
            await lease.AcquireAsync(leaseDuration, cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (IsLeaseHeldElsewhere(ex))
        {
            return false;
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "BlobNotFound")
        {
            // First use of this key.
        }

        await CreateGuardAsync(blob, ct).ConfigureAwait(false);

        try
        {
            await lease.AcquireAsync(leaseDuration, cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (IsLeaseHeldElsewhere(ex))
        {
            return false;
        }
    }

    private static async Task CreateGuardAsync(BlobClient blob, CancellationToken ct)
    {
        try
        {
            // Create-only: an existing guard, its content and its metadata are never touched.
            await blob.UploadAsync(
                BinaryData.Empty,
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            // Someone else created it first, and may have leased it already.
        }
    }

    private static async Task<long> IssueFencingTokenAsync(BlobClient blob, string leaseId, CancellationToken ct)
    {
        // Only the lease holder can write the guard, so read-increment-write under the lease is atomic per key.
        var properties = await blob.GetPropertiesAsync(new BlobRequestConditions { LeaseId = leaseId }, ct).ConfigureAwait(false);
        var metadata = new Dictionary<string, string>(properties.Value.Metadata, StringComparer.OrdinalIgnoreCase);

        var last = metadata.TryGetValue(FenceMetadataName, out var value)
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

        var next = checked(last + 1);
        metadata[FenceMetadataName] = next.ToString(CultureInfo.InvariantCulture);

        await blob.SetMetadataAsync(
            metadata,
            new BlobRequestConditions { LeaseId = leaseId, IfMatch = properties.Value.ETag },
            ct).ConfigureAwait(false);

        return next;
    }
}

/// <summary>One held blob lease.</summary>
internal sealed class HeldBlobLease(BlobClient blob, BlobLeaseClient lease) : ILockLease
{
    public BlobClient Blob { get; } = blob;

    public string LeaseId => lease.LeaseId;

    public long? FencingToken { get; set; }

    public async Task<bool> RenewAsync(CancellationToken ct)
    {
        try
        {
            await lease.RenewAsync(cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (IsGone(ex))
        {
            return false;
        }
    }

    public async Task ReleaseAsync(CancellationToken ct)
    {
        try
        {
            await lease.ReleaseAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (IsGone(ex))
        {
            // Already expired, broken or taken: nothing left to release.
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal async Task ReleaseQuietlyAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ReleaseAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RequestFailedException or OperationCanceledException or AggregateException)
        {
            // It expires on its own.
        }
    }

    // 409 on a lease operation means another lease id, no lease, or a broken lease; 404 means the guard is gone.
    internal static bool IsGone(RequestFailedException exception) => exception.Status is 409 or 404;
}
