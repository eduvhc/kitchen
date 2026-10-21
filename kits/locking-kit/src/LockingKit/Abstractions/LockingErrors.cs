using ResultingKit;

namespace LockingKit;

/// <summary>
/// The <see cref="Error.Code"/>s every provider returns, so callers branch on one set whichever backend is behind
/// <see cref="IDistributedLock"/>. Messages may name the key; codes never do.
/// </summary>
public static class LockingErrors
{
    /// <summary><see cref="ErrorKind.Conflict"/>: someone else holds the lock.</summary>
    public const string Busy = "locking.busy";

    /// <summary><see cref="ErrorKind.Conflict"/>: the lock stayed held for the whole wait.</summary>
    public const string Timeout = "locking.timeout";

    /// <summary><see cref="ErrorKind.Validation"/>: the key breaks the rules on <see cref="IDistributedLock"/>.</summary>
    public const string InvalidKey = "locking.invalid_key";

    /// <summary><see cref="ErrorKind.Validation"/>: another argument is out of range, such as a negative timeout.</summary>
    public const string InvalidArgument = "locking.invalid_argument";

    /// <summary>
    /// <see cref="ErrorKind.Transient"/>: the backend could not be reached, timed out, throttled or failed. Retrying may
    /// help; the provider's SDK has usually retried already.
    /// </summary>
    public const string Unavailable = "locking.unavailable";

    /// <summary>
    /// <see cref="ErrorKind.Failure"/>: the backend refused the provider's own setup — wrong credentials, a denied
    /// permission, a container or database that does not exist. No retry fixes it; fix the configuration.
    /// </summary>
    public const string Misconfigured = "locking.misconfigured";

    /// <summary>
    /// <see cref="ErrorKind.Failure"/>: the backend failed in a way no provider classifies. The message carries the
    /// exception type and message.
    /// </summary>
    public const string BackendFailed = "locking.backend_failed";

    /// <summary><see cref="ErrorKind.Failure"/>: the lock provider was disposed, usually because the host is shutting down.</summary>
    public const string Disposed = "locking.disposed";

    /// <summary>
    /// <see cref="ErrorKind.Conflict"/>: from <see cref="ILockHandle.EnsureHeld"/>, the handle no longer holds its
    /// lock — it was lost or released.
    /// </summary>
    public const string Lost = "locking.lost";

    /// <summary>A <see cref="Busy"/> error for <paramref name="key"/>.</summary>
    /// <param name="key">The lock key.</param>
    public static Error Held(string key) => Error.Conflict(Busy, $"The lock '{key}' is held by someone else.");

    /// <summary>A <see cref="Timeout"/> error for <paramref name="key"/>.</summary>
    /// <param name="key">The lock key.</param>
    /// <param name="waited">How long the caller waited.</param>
    public static Error WaitTimedOut(string key, TimeSpan waited) =>
        Error.Conflict(Timeout, $"The lock '{key}' was still held after waiting {waited.TotalSeconds:0.###} s.");

    /// <summary>An <see cref="Unavailable"/> error.</summary>
    /// <param name="detail">What the provider said.</param>
    public static Error BackendUnavailable(string detail) =>
        Error.Transient(Unavailable, $"The lock backend is unavailable: {detail}");

    /// <summary>A <see cref="Misconfigured"/> error.</summary>
    /// <param name="detail">What the provider said.</param>
    public static Error Misconfiguration(string detail) =>
        Error.Failure(Misconfigured, $"The lock backend rejected the provider's configuration: {detail}");

    /// <summary>A <see cref="BackendFailed"/> error for an exception nothing classified.</summary>
    /// <param name="exception">What the backend threw.</param>
    public static Error Unexpected(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Error.Failure(BackendFailed, $"The lock backend failed: {exception.GetType().FullName}: {exception.Message}");
    }

    /// <summary>A <see cref="Lost"/> error for <paramref name="key"/>.</summary>
    /// <param name="key">The lock key.</param>
    /// <param name="reason">Why it was lost, or <see langword="null"/> when it was released.</param>
    public static Error NoLongerHeld(string key, LockLossReason? reason) =>
        Error.Conflict(Lost, reason is { } r ? $"The lock '{key}' was lost ({r})." : $"The lock '{key}' was released.");

    internal static Error ProviderDisposed(string name) =>
        Error.Failure(Disposed, $"The lock provider '{name}' has been disposed.");

    internal static Error BadKey(string reason) => Error.Validation(InvalidKey, $"Invalid lock key: {reason}");

    internal static Error BadArgument(string reason) => Error.Validation(InvalidArgument, reason);
}
