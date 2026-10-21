using ResultingKit;

namespace LockingKit;

/// <summary>
/// What a provider implements: one attempt at a lock, and the lease it returns. LockingKit's engine does everything
/// else — key validation, waiting and backoff, renewal scheduling, loss detection, bounded release, results and
/// telemetry — the same way for every backend. Register one with <see cref="LockingBuilder.UseBackend{TOptions}"/>.
/// </summary>
public interface ILockBackend : IAsyncDisposable
{
    /// <summary>How the backend keeps a lock alive, which decides when the engine renews and when it gives up.</summary>
    LeaseTiming Timing { get; }

    /// <summary>
    /// Makes one attempt at <paramref name="key"/>. Never waits for a holder.
    /// </summary>
    /// <param name="key">A key the engine has already validated.</param>
    /// <param name="ct">Cancels the attempt.</param>
    /// <returns>The lease when acquired; <see langword="null"/> when someone else holds the lock.</returns>
    /// <remarks>
    /// Throw on any other outcome; the engine turns every exception into a result through
    /// <see cref="TranslateException"/>, so none reaches the caller.
    /// </remarks>
    Task<ILockLease?> TryAcquireAsync(string key, CancellationToken ct);

    /// <summary>
    /// Classifies an exception from <see cref="TryAcquireAsync"/>: <see cref="LockingErrors.BackendUnavailable"/> for
    /// throttling, timeouts and outages, <see cref="LockingErrors.Misconfiguration"/> for credentials, permissions and
    /// missing resources. Return <see langword="null"/> to leave it to the engine, which reports timeouts and socket
    /// failures as unavailable and anything else as <see cref="LockingErrors.BackendFailed"/>.
    /// </summary>
    /// <param name="exception">What the attempt threw.</param>
    Error? TranslateException(Exception exception);
}

/// <summary>One held lock, as a backend sees it. The engine renews it, releases it, then disposes it.</summary>
public interface ILockLease : IAsyncDisposable
{
    /// <summary>The fencing token issued with this lease, or <see langword="null"/> when the backend issues none.</summary>
    long? FencingToken { get; }

    /// <summary>
    /// Extends the lease (an expiring backend) or confirms it is still held (a connection-held backend).
    /// </summary>
    /// <param name="ct">Cancelled when the attempt runs out of time or the handle is disposed.</param>
    /// <returns>
    /// <see langword="true"/> when the lock is still held; <see langword="false"/> when the backend reports that this
    /// lease no longer holds it. Throw when it could not tell — the engine retries until the lease would expire.
    /// </returns>
    Task<bool> RenewAsync(CancellationToken ct);

    /// <summary>
    /// Gives the lock up. Called once, after renewal has stopped, with a short timeout. A lease that is no longer held
    /// should return quietly; failures are recorded and swallowed, since expiry frees the lock anyway.
    /// </summary>
    /// <param name="ct">Cancelled when the release timeout elapses.</param>
    Task ReleaseAsync(CancellationToken ct);
}
