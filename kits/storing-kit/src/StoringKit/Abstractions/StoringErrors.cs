using ResultingKit;

namespace StoringKit;

/// <summary>
/// The <see cref="Error.Code"/>s every provider returns, so callers branch on one set whichever store is behind
/// <see cref="IObjectStore"/>. Messages may name the key; codes never do.
/// </summary>
public static class StoringErrors
{
    /// <summary><see cref="ErrorKind.NotFound"/>: no object exists at the key.</summary>
    public const string NotFound = "storing.not_found";

    /// <summary><see cref="ErrorKind.Conflict"/>: a <see cref="WriteCondition.IfNotExists"/> write found an object already there.</summary>
    public const string AlreadyExists = "storing.already_exists";

    /// <summary>
    /// <see cref="ErrorKind.Conflict"/>: a <see cref="WriteCondition.IfMatch"/> write found a different version, or no
    /// object at all.
    /// </summary>
    public const string PreconditionFailed = "storing.precondition_failed";

    /// <summary><see cref="ErrorKind.Validation"/>: the key breaks the rules on <see cref="IObjectStore"/>.</summary>
    public const string InvalidKey = "storing.invalid_key";

    /// <summary><see cref="ErrorKind.Validation"/>: user metadata breaks the rules on <see cref="PutObjectOptions.Metadata"/>.</summary>
    public const string InvalidMetadata = "storing.invalid_metadata";

    /// <summary>
    /// <see cref="ErrorKind.Validation"/>: another argument is out of range — a content type, a checksum that is not a
    /// Base64 MD5, a page size, a URL lifetime.
    /// </summary>
    public const string InvalidArgument = "storing.invalid_argument";

    /// <summary><see cref="ErrorKind.Validation"/>: the service received content that does not match <see cref="PutObjectOptions.ContentMd5"/>.</summary>
    public const string ChecksumMismatch = "storing.checksum_mismatch";

    /// <summary><see cref="ErrorKind.Validation"/>: the content is larger than the provider accepts in one write.</summary>
    public const string TooLarge = "storing.too_large";

    /// <summary><see cref="ErrorKind.Transient"/>: the service asked the caller to slow down (429, 503 SlowDown, ServerBusy).</summary>
    public const string Throttled = "storing.throttled";

    /// <summary><see cref="ErrorKind.Transient"/>: the service failed (5xx) or could not be reached.</summary>
    public const string Unavailable = "storing.unavailable";

    /// <summary><see cref="ErrorKind.Transient"/>: the request timed out.</summary>
    public const string Timeout = "storing.timeout";

    /// <summary>
    /// The error for a write whose condition did not hold: <see cref="AlreadyExists"/> for a create-only write,
    /// <see cref="PreconditionFailed"/> otherwise.
    /// </summary>
    /// <param name="key">The key written.</param>
    /// <param name="condition">The condition that failed.</param>
    public static Error ConditionNotMet(string key, WriteCondition? condition) =>
        condition is { IsCreateOnly: true }
            ? Error.Conflict(AlreadyExists, $"An object already exists at '{key}'.")
            : Error.Conflict(PreconditionFailed, $"The object at '{key}' no longer has the expected ETag.");

    /// <summary>A <see cref="NotFound"/> error for <paramref name="key"/>.</summary>
    /// <param name="key">The missing key.</param>
    public static Error MissingObject(string key) => Error.NotFound(NotFound, $"No object exists at '{key}'.");

    /// <summary>A <see cref="ChecksumMismatch"/> error for <paramref name="key"/>.</summary>
    /// <param name="key">The key written.</param>
    public static Error CorruptUpload(string key) =>
        Error.Validation(ChecksumMismatch, $"The content received for '{key}' does not match its MD5.");

    /// <summary>A <see cref="TooLarge"/> error for <paramref name="key"/>.</summary>
    /// <param name="key">The key written.</param>
    public static Error ContentTooLarge(string key) =>
        Error.Validation(TooLarge, $"The content for '{key}' is larger than the store accepts in one write.");

    /// <summary>A <see cref="Throttled"/> error.</summary>
    /// <param name="detail">What the provider said.</param>
    public static Error Throttling(string detail) => Error.Transient(Throttled, $"The store is throttling requests: {detail}");

    /// <summary>An <see cref="Unavailable"/> error.</summary>
    /// <param name="detail">What the provider said.</param>
    public static Error ServiceUnavailable(string detail) => Error.Transient(Unavailable, $"The store is unavailable: {detail}");

    /// <summary>A <see cref="Timeout"/> error.</summary>
    /// <param name="detail">What timed out.</param>
    public static Error TimedOut(string detail) => Error.Transient(Timeout, $"The request to the store timed out: {detail}");

    internal static Error BadKey(string reason) => Error.Validation(InvalidKey, $"Invalid object key: {reason}");

    internal static Error BadMetadata(string reason) => Error.Validation(InvalidMetadata, $"Invalid metadata: {reason}");

    internal static Error BadArgument(string reason) => Error.Validation(InvalidArgument, reason);
}
