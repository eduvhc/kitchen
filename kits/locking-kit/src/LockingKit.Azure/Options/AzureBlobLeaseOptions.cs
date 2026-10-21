using Azure.Core;

namespace LockingKit.Azure;

/// <summary>
/// Settings for locks held as Azure blob leases. Bound from <c>Locking:{name}</c> by
/// <c>UseAzureBlobLeases(configuration)</c>.
/// </summary>
/// <remarks>
/// Each key is a guard blob under <see cref="BlobPrefix"/> in <see cref="ContainerName"/>. A missing guard is created
/// empty, create-only; an existing one is never overwritten, and its content and metadata are kept. The identity needs
/// to read, create and lease blobs in the container (<c>Storage Blob Data Contributor</c>).
/// </remarks>
public sealed class AzureBlobLeaseOptions : LockingOptions
{
    /// <summary>
    /// A storage account connection string: account key, SAS, or <c>UseDevelopmentStorage=true</c> for Azurite.
    /// Keep it in a secret store or an environment variable, never in <c>appsettings.json</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The blob service endpoint, <c>https://{account}.blob.core.windows.net</c>, used with <see cref="Credential"/>
    /// instead of <see cref="ConnectionString"/>.
    /// </summary>
    public Uri? ServiceUri { get; set; }

    /// <summary>The identity used with <see cref="ServiceUri"/>, such as <c>DefaultAzureCredential</c>. Code only.</summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>The container that holds the guard blobs. Required; it must already exist.</summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Where guard blobs go inside the container: <c>{BlobPrefix}{escaped key}</c>. Empty, or ending with <c>/</c>.
    /// Defaults to <c>locks/</c>.
    /// </summary>
    public string BlobPrefix { get; set; } = "locks/";

    /// <summary>
    /// How long a lease lasts without renewal: 15 to 60 seconds, the range Azure accepts. A crashed holder's lock is
    /// free again at most this long after its last renewal. Defaults to 30 s.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Issues a <see cref="ILockHandle.FencingToken"/> with every lease: a counter in the guard blob's
    /// <c>lockingkit_fence</c> metadata, incremented under the lease. Costs two more requests per acquisition.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool FencingTokens { get; set; }

    /// <summary>
    /// How long one request to Azure may take before the SDK retries it (twice at most). Defaults to 5 s. Renewals are
    /// bounded by the lease deadline as well.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
