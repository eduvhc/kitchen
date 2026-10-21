using ResultingKit;

namespace LockingKit;

/// <summary>
/// Mutual exclusion across processes and machines, keyed by string. Every provider — Azure blob leases, PostgreSQL
/// advisory locks, in-memory — honours the same contract, so code written against it moves between them.
/// </summary>
/// <remarks>
/// <para>
/// Contention is an expected outcome, not an exception: a held lock comes back as a failed <see cref="Result"/> with
/// <see cref="LockingErrors.Busy"/> or, after waiting, <see cref="LockingErrors.Timeout"/>, both
/// <see cref="ErrorKind.Conflict"/>. A backend that cannot be reached is <see cref="LockingErrors.Unavailable"/>
/// (<see cref="ErrorKind.Transient"/>), and a bad key is <see cref="LockingErrors.InvalidKey"/>.
/// </para>
/// <para>
/// Misconfiguration — wrong credentials, a container or database that does not exist — throws the provider's
/// exception, because no retry fixes it. Cancelling <c>ct</c> throws <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// Keys are any string of 1 to <see cref="DistributedLocks.MaxKeyLength"/> characters, compared ordinally
/// (case-sensitive). Each provider maps them to what its backend accepts; see the provider's documentation.
/// Locks are not reentrant: acquiring a key this process already holds is busy, like acquiring it anywhere else.
/// </para>
/// </remarks>
public interface IDistributedLock
{
    /// <summary>Takes the lock on <paramref name="key"/> if it is free, without waiting.</summary>
    /// <param name="key">The lock key.</param>
    /// <param name="ct">Cancels the attempt. It does not affect the handle once acquired.</param>
    /// <returns>
    /// A handle that holds the lock until disposed; or <see cref="LockingErrors.Busy"/> when someone else holds it.
    /// </returns>
    Task<Result<ILockHandle>> TryAcquireAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Takes the lock on <paramref name="key"/>, waiting up to <paramref name="timeout"/> for it to become free.
    /// Waiters in one process are served in arrival order and are woken as soon as a local holder releases; waiters in
    /// different processes retry with capped, jittered backoff, so a long waiter polls as often as a new one.
    /// </summary>
    /// <param name="key">The lock key.</param>
    /// <param name="timeout">
    /// How long to wait. <see cref="TimeSpan.Zero"/> tries once, like <see cref="TryAcquireAsync"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> waits until <paramref name="ct"/> is cancelled.
    /// </param>
    /// <param name="ct">Cancels the wait. It does not affect the handle once acquired.</param>
    /// <returns>
    /// A handle that holds the lock until disposed; <see cref="LockingErrors.Timeout"/> when the lock stayed held; or
    /// <see cref="LockingErrors.Unavailable"/> when the last attempt could not reach the backend.
    /// </returns>
    Task<Result<ILockHandle>> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Conveniences over <see cref="IDistributedLock"/>.</summary>
public static class DistributedLockExtensions
{
    /// <summary>Takes the lock on <paramref name="key"/>, waiting for as long as it takes, until <paramref name="ct"/> is cancelled.</summary>
    /// <param name="locks">The lock provider.</param>
    /// <param name="key">The lock key.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>A handle; or <see cref="LockingErrors.Unavailable"/> when the backend cannot be reached.</returns>
    public static Task<Result<ILockHandle>> AcquireAsync(this IDistributedLock locks, string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        return locks.AcquireAsync(key, Timeout.InfiniteTimeSpan, ct);
    }
}
