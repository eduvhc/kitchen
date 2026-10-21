using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace LockingKit.Azure;

/// <summary>The guard blob and lease id behind a handle from the Azure provider.</summary>
/// <param name="Blob">The guard blob.</param>
/// <param name="LeaseId">The lease id the handle holds.</param>
public sealed record AzureBlobLease(BlobClient Blob, string LeaseId)
{
    /// <summary>
    /// Conditions for writing to the guard blob itself while the lease is held: Azure rejects the write once the lease
    /// is lost, which makes the guard's own content safely fenced.
    /// </summary>
    public BlobRequestConditions Conditions => new() { LeaseId = LeaseId };
}

/// <summary>Azure-specific access to a held lock.</summary>
public static class AzureBlobLockHandleExtensions
{
    /// <summary>
    /// The guard blob and lease id behind <paramref name="handle"/>, for writes to the guard blob through the Azure
    /// SDK; or <see langword="null"/> when the handle is not from the Azure provider.
    /// </summary>
    /// <remarks>
    /// The lease protects writes to the guard blob only. Writes to anything else are not made safe by holding it; use
    /// <see cref="ILockHandle.FencingToken"/> or the target's own conditional writes for those.
    /// </remarks>
    /// <param name="handle">A handle from an Azure-backed <see cref="IDistributedLock"/>.</param>
    public static AzureBlobLease? GetAzureBlobLease(this ILockHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        return handle is LockHandle { Lease: HeldBlobLease lease } ? new AzureBlobLease(lease.Blob, lease.LeaseId) : null;
    }
}
