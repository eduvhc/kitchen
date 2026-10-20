using ResultingKit;

namespace StoringKit;

/// <summary>
/// A bucket or container of objects, addressed by key. Every provider — Azure Blob Storage, AWS S3,
/// Cloudflare R2, MinIO — honours the same contract, so code written against it moves between them.
/// </summary>
/// <remarks>
/// <para>
/// Expected failures come back as a failed <see cref="Result"/>: a missing object is
/// <see cref="ErrorKind.NotFound"/>, a conditional write that lost is <see cref="ErrorKind.Conflict"/>,
/// bad input is <see cref="ErrorKind.Validation"/>, and throttling, timeouts and server errors are
/// <see cref="ErrorKind.Transient"/> — the only kind worth retrying. The codes are on <see cref="StoringErrors"/>.
/// </para>
/// <para>
/// Faults that mean the store itself is misconfigured — wrong credentials, a bucket or container that does not
/// exist, a denied permission — throw the provider SDK's exception, because no retry or input change fixes them.
/// </para>
/// <para>
/// Keys are case-sensitive, at most 1024 UTF-8 bytes, use <c>/</c> as a separator without leading, trailing or
/// doubled separators, and contain no <c>\</c>, control characters or <c>.</c>/<c>..</c> segments.
/// </para>
/// </remarks>
public interface IObjectStore
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="key"/>, replacing any existing object unless
    /// <see cref="PutObjectOptions.Condition"/> says otherwise.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="content">
    /// The bytes to store, read from the current position to the end. Not disposed. A seekable stream is read twice:
    /// once to compute its MD5, once to upload it.
    /// </param>
    /// <param name="options">Content type, metadata, checksum and write condition.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>
    /// A receipt with the new ETag; or <see cref="StoringErrors.AlreadyExists"/> /
    /// <see cref="StoringErrors.PreconditionFailed"/> when the condition did not hold.
    /// </returns>
    Task<Result<PutReceipt>> PutAsync(string key, Stream content, PutObjectOptions? options = null, CancellationToken ct = default);

    /// <summary>Opens <paramref name="key"/> for reading.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>
    /// The object's properties and a stream over its content, which the caller disposes; or
    /// <see cref="StoringErrors.NotFound"/>.
    /// </returns>
    Task<Result<ObjectContent>> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Reads the properties and metadata of <paramref name="key"/> without its content.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The properties; or <see cref="StoringErrors.NotFound"/>.</returns>
    Task<Result<StoredObject>> GetPropertiesAsync(string key, CancellationToken ct = default);

    /// <summary>Checks whether <paramref name="key"/> exists. A missing object is a success carrying <see langword="false"/>.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="ct">Cancels the request.</param>
    Task<Result<bool>> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes <paramref name="key"/>. Deleting an object that does not exist succeeds, so a retried delete is safe.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="ct">Cancels the request.</param>
    Task<Result> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Lists one page of objects whose key starts with <paramref name="prefix"/>, in ascending key order. The listing
    /// is flat: <c>/</c> is not treated as a folder boundary.
    /// </summary>
    /// <param name="prefix">The key prefix, or <see langword="null"/> for every object.</param>
    /// <param name="options">Page size and the continuation token from the previous page.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The page; <see cref="ObjectPage.ContinuationToken"/> is set when there are more.</returns>
    Task<Result<ObjectPage>> ListAsync(string? prefix = null, ListObjectsOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a URL anyone holding it can use to read <paramref name="key"/> until it expires: a SAS on Azure, a
    /// presigned URL on S3. The object is not checked for existence; a URL to a missing object returns 404 when used.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="validFor">How long the URL works. Greater than zero and at most <see cref="ObjectStore.MaxReadUrlLifetime"/>.</param>
    /// <param name="ct">Cancels the request, where signing needs one (an Azure user delegation key).</param>
    /// <returns>The URL; or a validation error for a bad key or lifetime.</returns>
    /// <exception cref="InvalidOperationException">The store has no credentials it can sign with.</exception>
    Task<Result<Uri>> GetReadUrlAsync(string key, TimeSpan validFor, CancellationToken ct = default);

    /// <summary>
    /// Builds the permanent, unsigned URL of <paramref name="key"/> under <see cref="ObjectStoreOptions.PublicBaseUrl"/>.
    /// Nothing is sent to the provider. Whether the URL is actually readable depends on how the bucket or container
    /// was set up; StoringKit never changes access settings.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <returns>The URL; or a validation error for a bad key.</returns>
    /// <exception cref="InvalidOperationException">The store has no <see cref="ObjectStoreOptions.PublicBaseUrl"/>.</exception>
    Result<Uri> GetPublicUrl(string key);
}
