namespace LockingKit;

/// <summary>
/// A held lock. It renews itself in the background and is released when disposed. Dispose it with
/// <c>await using</c>; a crashed process's lock is freed by the backend when its lease expires or its connection ends.
/// </summary>
/// <remarks>
/// Holding a handle means the lock was held at the last renewal, not that it is held now: a process can pause (GC,
/// VM suspension) past the lease without noticing. Stop work on <see cref="LostToken"/>, and pass
/// <see cref="FencingToken"/> to the resource you protect when you need correctness across pauses.
/// </remarks>
public interface ILockHandle : IAsyncDisposable
{
    /// <summary>The key this handle holds.</summary>
    string Key { get; }

    /// <summary>
    /// Cancelled as soon as the lock is no longer held: lost (see <see cref="LossReason"/>), or released by disposing
    /// the handle. Link it into the work the lock protects so the work stops instead of continuing unprotected.
    /// </summary>
    CancellationToken LostToken { get; }

    /// <summary><see langword="true"/> once the lock has been lost. A normal release does not count as a loss.</summary>
    bool IsLost { get; }

    /// <summary>Why the lock was lost, or <see langword="null"/> while it is held or after a normal release.</summary>
    LockLossReason? LossReason { get; }

    /// <summary>
    /// A number that is larger for every later holder of the same key, when the provider issues them (see its
    /// documentation); otherwise <see langword="null"/>. Have the protected resource reject writes carrying a token
    /// lower than the highest it has seen, and a paused former holder can no longer overwrite its successor.
    /// </summary>
    long? FencingToken { get; }

    /// <summary>
    /// Checks the lock is still held before a step that must not run unprotected, as a result: success while held,
    /// <see cref="LockingErrors.Lost"/> (<see cref="ResultingKit.ErrorKind.Conflict"/>) once it was lost or released.
    /// </summary>
    ResultingKit.Result EnsureHeld();

    /// <summary>
    /// The cancellation-style form of <see cref="EnsureHeld"/>, for work that already stops on
    /// <see cref="OperationCanceledException"/>: throws <see cref="LockLostException"/> when the lock has been lost,
    /// and <see cref="ObjectDisposedException"/> after the handle was disposed.
    /// </summary>
    void ThrowIfLost();
}

/// <summary>Why a <see cref="ILockHandle"/> stopped holding its lock.</summary>
public enum LockLossReason
{
    /// <summary>
    /// The lease could not be renewed before it would expire: the backend was unreachable, renewals kept failing, or
    /// the process was paused. The handle gives up a safety margin before the backend's own expiry.
    /// </summary>
    Expired,

    /// <summary>The backend reported that this handle no longer holds the lock: the lease was broken, taken or deleted.</summary>
    Revoked,

    /// <summary>The connection that held the lock failed or could not confirm it in time (connection-held backends such as PostgreSQL).</summary>
    ConnectionLost,
}
