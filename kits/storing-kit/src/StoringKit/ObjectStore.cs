using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using ResultingKit;

namespace StoringKit;

/// <summary>
/// The base every provider derives from. It validates keys, metadata and arguments, computes MD5s, and turns
/// exceptions the provider classifies into <see cref="Result"/> failures, so each provider only talks to its SDK.
/// </summary>
/// <remarks>
/// The <c>…CoreAsync</c> methods receive validated input and throw on failure. <see cref="TranslateException"/>
/// decides which exceptions are expected outcomes; anything it returns <see langword="null"/> for propagates.
/// Cancellation requested by the caller always propagates as <see cref="OperationCanceledException"/>.
/// </remarks>
/// <param name="publicBaseUrl">The store's <see cref="ObjectStoreOptions.PublicBaseUrl"/>, if it is public.</param>
/// <param name="timeProvider">The clock read URLs expire against. Defaults to <see cref="TimeProvider.System"/>.</param>
public abstract class ObjectStore(Uri? publicBaseUrl, TimeProvider? timeProvider = null) : IObjectStore
{
    /// <summary>The longest key, in UTF-8 bytes, that every provider accepts.</summary>
    public const int MaxKeyBytes = 1024;

    /// <summary>
    /// The most user metadata, as the sum of name and value lengths, that every provider accepts with room for the
    /// providers' own entries. S3 and R2 cap all metadata at 2 KB.
    /// </summary>
    public const int MaxMetadataBytes = 2000;

    /// <summary>The content type stored when <see cref="PutObjectOptions.ContentType"/> is not set.</summary>
    public const string DefaultContentType = "application/octet-stream";

    private readonly Uri? _publicBaseUrl = WithTrailingSlash(publicBaseUrl);

    /// <summary>The longest lifetime <see cref="GetReadUrlAsync"/> accepts: seven days, the SigV4 presigning limit.</summary>
    public static TimeSpan MaxReadUrlLifetime { get; } = TimeSpan.FromDays(7);

    /// <summary>The clock read URLs expire against.</summary>
    protected TimeProvider TimeProvider { get; } = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<Result<PutReceipt>> PutAsync(
        string key,
        Stream content,
        PutObjectOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanRead)
        {
            throw new ArgumentException("The content stream is not readable.", nameof(content));
        }

        if (ObjectKeys.Validate(key) is { } keyError)
        {
            return keyError;
        }

        options ??= new PutObjectOptions();

        var contentType = options.ContentType ?? DefaultContentType;

        if (!IsValidContentType(contentType))
        {
            return StoringErrors.BadArgument($"'{contentType}' is not a valid content type.");
        }

        var metadata = ObjectMetadata.Normalize(options.Metadata, out var metadataError);

        if (metadataError is not null)
        {
            return metadataError;
        }

        if (options.ContentMd5 is { } suppliedMd5 && !IsBase64Md5(suppliedMd5))
        {
            return StoringErrors.BadArgument("ContentMd5 must be the Base64 encoding of a 16-byte MD5 hash.");
        }

        var length = content.CanSeek ? content.Length - content.Position : (long?)null;
        var condition = options.Condition;

        try
        {
            var md5 = options.ContentMd5 ?? (content.CanSeek ? await ComputeMd5Async(content, ct).ConfigureAwait(false) : null);
            var write = new ObjectWrite(key, content, contentType, metadata, md5, condition, length);

            return await PutCoreAsync(write, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, ObjectStoreOperation.Put, key, condition, ct) is { } error)
        {
            return error;
        }
    }

    /// <inheritdoc />
    public async Task<Result<ObjectContent>> GetAsync(string key, CancellationToken ct = default)
    {
        if (ObjectKeys.Validate(key) is { } keyError)
        {
            return keyError;
        }

        try
        {
            return await GetCoreAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, ObjectStoreOperation.Get, key, null, ct) is { } error)
        {
            return error;
        }
    }

    /// <inheritdoc />
    public async Task<Result<StoredObject>> GetPropertiesAsync(string key, CancellationToken ct = default)
    {
        if (ObjectKeys.Validate(key) is { } keyError)
        {
            return keyError;
        }

        try
        {
            return await GetPropertiesCoreAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, ObjectStoreOperation.GetProperties, key, null, ct) is { } error)
        {
            return error;
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (ObjectKeys.Validate(key) is { } keyError)
        {
            return keyError;
        }

        try
        {
            return await ExistsCoreAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, ObjectStoreOperation.Exists, key, null, ct) is { } error)
        {
            return error;
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (ObjectKeys.Validate(key) is { } keyError)
        {
            return keyError;
        }

        try
        {
            await DeleteCoreAsync(key, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (Translate(ex, ObjectStoreOperation.Delete, key, null, ct) is { } error)
        {
            return error;
        }
    }

    /// <inheritdoc />
    public async Task<Result<ObjectPage>> ListAsync(
        string? prefix = null,
        ListObjectsOptions? options = null,
        CancellationToken ct = default)
    {
        prefix = string.IsNullOrEmpty(prefix) ? null : prefix;

        if (prefix is not null && ObjectKeys.ValidatePrefix(prefix) is { } prefixError)
        {
            return prefixError;
        }

        var pageSize = options?.PageSize ?? ListObjectsOptions.MaxPageSize;

        if (pageSize is < 1 or > ListObjectsOptions.MaxPageSize)
        {
            return StoringErrors.BadArgument($"PageSize must be between 1 and {ListObjectsOptions.MaxPageSize}.");
        }

        var token = string.IsNullOrEmpty(options?.ContinuationToken) ? null : options.ContinuationToken;

        try
        {
            return await ListCoreAsync(prefix, pageSize, token, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, ObjectStoreOperation.List, prefix ?? string.Empty, null, ct) is { } error)
        {
            return error;
        }
    }

    /// <inheritdoc />
    public async Task<Result<Uri>> GetReadUrlAsync(string key, TimeSpan validFor, CancellationToken ct = default)
    {
        if (ObjectKeys.Validate(key) is { } keyError)
        {
            return keyError;
        }

        if (validFor <= TimeSpan.Zero || validFor > MaxReadUrlLifetime)
        {
            return StoringErrors.BadArgument($"A read URL must be valid for more than zero and at most {MaxReadUrlLifetime.TotalDays:0} days.");
        }

        var expiresOn = TimeProvider.GetUtcNow() + validFor;

        try
        {
            return await GetReadUrlCoreAsync(key, expiresOn, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, ObjectStoreOperation.GetReadUrl, key, null, ct) is { } error)
        {
            return error;
        }
    }

    /// <inheritdoc />
    public Result<Uri> GetPublicUrl(string key)
    {
        if (ObjectKeys.Validate(key) is { } keyError)
        {
            return keyError;
        }

        if (_publicBaseUrl is null)
        {
            throw new InvalidOperationException(
                "This store has no PublicBaseUrl. Configure one for a bucket or container that is publicly readable, or use GetReadUrlAsync.");
        }

        var path = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
        return new Uri(_publicBaseUrl, path);
    }

    /// <summary>Writes a validated object. Throws on failure.</summary>
    /// <param name="write">The key, content and everything that goes with it.</param>
    /// <param name="ct">Cancels the upload.</param>
    protected abstract Task<PutReceipt> PutCoreAsync(ObjectWrite write, CancellationToken ct);

    /// <summary>Opens an object for reading. Throws on failure, including when the object does not exist.</summary>
    /// <param name="key">A validated key.</param>
    /// <param name="ct">Cancels the request.</param>
    protected abstract Task<ObjectContent> GetCoreAsync(string key, CancellationToken ct);

    /// <summary>Reads an object's properties. Throws on failure, including when the object does not exist.</summary>
    /// <param name="key">A validated key.</param>
    /// <param name="ct">Cancels the request.</param>
    protected abstract Task<StoredObject> GetPropertiesCoreAsync(string key, CancellationToken ct);

    /// <summary>Checks whether an object exists. Throws on failure.</summary>
    /// <param name="key">A validated key.</param>
    /// <param name="ct">Cancels the request.</param>
    protected abstract Task<bool> ExistsCoreAsync(string key, CancellationToken ct);

    /// <summary>Deletes an object, succeeding when it does not exist. Throws on failure.</summary>
    /// <param name="key">A validated key.</param>
    /// <param name="ct">Cancels the request.</param>
    protected abstract Task DeleteCoreAsync(string key, CancellationToken ct);

    /// <summary>Lists one page of objects in ascending key order. Throws on failure.</summary>
    /// <param name="prefix">A validated prefix, or <see langword="null"/> for every object.</param>
    /// <param name="pageSize">The most objects to return, 1 to <see cref="ListObjectsOptions.MaxPageSize"/>.</param>
    /// <param name="continuationToken">The previous page's token, or <see langword="null"/>.</param>
    /// <param name="ct">Cancels the request.</param>
    protected abstract Task<ObjectPage> ListCoreAsync(string? prefix, int pageSize, string? continuationToken, CancellationToken ct);

    /// <summary>Signs a read URL. Throws on failure.</summary>
    /// <param name="key">A validated key.</param>
    /// <param name="expiresOn">When the URL stops working; within <see cref="MaxReadUrlLifetime"/> of now.</param>
    /// <param name="ct">Cancels the request.</param>
    protected abstract Task<Uri> GetReadUrlCoreAsync(string key, DateTimeOffset expiresOn, CancellationToken ct);

    /// <summary>
    /// Classifies a provider exception. Return an <see cref="Error"/> for an expected outcome — a missing object, a
    /// failed condition, throttling — or <see langword="null"/> to let it propagate. Exceptions this returns
    /// <see langword="null"/> for still pass through <see cref="TranslateTransportException"/>.
    /// </summary>
    /// <param name="exception">What the core method threw.</param>
    /// <param name="operation">The operation that failed.</param>
    /// <param name="key">The key, or the list prefix.</param>
    /// <param name="condition">The write condition, for <see cref="ObjectStoreOperation.Put"/>.</param>
    protected abstract Error? TranslateException(Exception exception, ObjectStoreOperation operation, string key, WriteCondition? condition);

    /// <summary>
    /// Classifies failures below the provider SDK: timeouts and connection failures are
    /// <see cref="ErrorKind.Transient"/>. Unwraps <see cref="AggregateException"/>s from SDK retry loops.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>A transient error, or <see langword="null"/>.</returns>
    protected static Error? TranslateTransportException(Exception exception) => exception switch
    {
        AggregateException { InnerExceptions.Count: > 0 } aggregate =>
            TranslateTransportException(aggregate.Flatten().InnerExceptions[^1]),
        TimeoutException or OperationCanceledException => StoringErrors.TimedOut(exception.Message),
        HttpRequestException or SocketException => StoringErrors.ServiceUnavailable(exception.Message),
        IOException { InnerException: SocketException or HttpRequestException } => StoringErrors.ServiceUnavailable(exception.Message),
        _ => null,
    };

    /// <summary>Classifies a failure while reading a stream <see cref="GetAsync"/> opened, after the provider has returned.</summary>
    internal static Error? TranslateReadException(Exception exception, CancellationToken ct) =>
        ct.IsCancellationRequested && exception is OperationCanceledException ? null : TranslateTransportException(exception);

    private Error? Translate(Exception exception, ObjectStoreOperation operation, string key, WriteCondition? condition, CancellationToken ct)
    {
        if (ct.IsCancellationRequested && exception is OperationCanceledException)
        {
            return null;
        }

        return TranslateException(exception, operation, key, condition) ?? TranslateTransportException(exception);
    }

    private static Uri? WithTrailingSlash(Uri? publicBaseUrl)
    {
        if (publicBaseUrl is null)
        {
            return null;
        }

        if (!publicBaseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The public base URL must be absolute.", nameof(publicBaseUrl));
        }

        // Without the trailing slash, combining would replace the last path segment.
        var text = publicBaseUrl.AbsoluteUri;
        return text.EndsWith('/') ? publicBaseUrl : new Uri(text + "/");
    }

    [SuppressMessage("Security", "CA5351:Do not use broken cryptographic algorithms",
        Justification = "MD5 is the integrity checksum Azure and S3 both verify (Content-MD5); it guards against corruption, not tampering.")]
    private static async Task<string> ComputeMd5Async(Stream content, CancellationToken ct)
    {
        var start = content.Position;
        var hash = await MD5.HashDataAsync(content, ct).ConfigureAwait(false);
        content.Position = start;
        return Convert.ToBase64String(hash);
    }

    private static bool IsBase64Md5(string value)
    {
        Span<byte> buffer = stackalloc byte[18];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 16;
    }

    private static bool IsValidContentType(string value) =>
        value.Length is > 2 and <= 255
        && value.IndexOf('/', StringComparison.Ordinal) > 0
        && value.All(c => c is >= ' ' and <= '~');
}

/// <summary>The operations on <see cref="IObjectStore"/>, for <see cref="ObjectStore.TranslateException"/>.</summary>
public enum ObjectStoreOperation
{
    /// <summary><see cref="IObjectStore.PutAsync"/>.</summary>
    Put,

    /// <summary><see cref="IObjectStore.GetAsync"/>.</summary>
    Get,

    /// <summary><see cref="IObjectStore.GetPropertiesAsync"/>.</summary>
    GetProperties,

    /// <summary><see cref="IObjectStore.ExistsAsync"/>.</summary>
    Exists,

    /// <summary><see cref="IObjectStore.DeleteAsync"/>.</summary>
    Delete,

    /// <summary><see cref="IObjectStore.ListAsync"/>.</summary>
    List,

    /// <summary><see cref="IObjectStore.GetReadUrlAsync"/>.</summary>
    GetReadUrl,
}

/// <summary>A validated write, as <see cref="ObjectStore"/> hands it to a provider.</summary>
public sealed class ObjectWrite
{
    internal ObjectWrite(
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        string? contentMd5,
        WriteCondition? condition,
        long? length)
    {
        Key = key;
        Content = content;
        ContentType = contentType;
        Metadata = metadata;
        ContentMd5 = contentMd5;
        Condition = condition;
        Length = length;
    }

    /// <summary>The validated key.</summary>
    public string Key { get; }

    /// <summary>The content, positioned where the upload starts.</summary>
    public Stream Content { get; }

    /// <summary>The content type to store.</summary>
    public string ContentType { get; }

    /// <summary>User metadata with lower-cased names.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>The Base64 MD5 to send for verification, when known.</summary>
    public string? ContentMd5 { get; }

    /// <summary>The write condition, or <see langword="null"/> for an unconditional write.</summary>
    public WriteCondition? Condition { get; }

    /// <summary>The number of bytes to upload, when the stream is seekable.</summary>
    public long? Length { get; }
}
