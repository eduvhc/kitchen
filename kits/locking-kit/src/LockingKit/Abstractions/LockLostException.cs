namespace LockingKit;

/// <summary>
/// Thrown by <see cref="ILockHandle.ThrowIfLost"/> once a lock has been lost. It is an
/// <see cref="OperationCanceledException"/> carrying <see cref="ILockHandle.LostToken"/>, so code that already treats
/// cancellation as "stop" stops here too.
/// </summary>
public sealed class LockLostException : OperationCanceledException
{
    /// <summary>Creates the exception for a lost lock.</summary>
    /// <param name="key">The key that was lost.</param>
    /// <param name="reason">Why it was lost.</param>
    /// <param name="token">The handle's <see cref="ILockHandle.LostToken"/>.</param>
    public LockLostException(string key, LockLossReason reason, CancellationToken token)
        : base($"The lock '{key}' was lost ({reason}).", token)
    {
        Key = key;
        Reason = reason;
    }

    /// <summary>Creates the exception with a message.</summary>
    public LockLostException()
        : this("The lock was lost.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public LockLostException(string message)
        : base(message)
    {
        Key = string.Empty;
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public LockLostException(string message, Exception innerException)
        : base(message, innerException)
    {
        Key = string.Empty;
    }

    /// <summary>The key that was lost.</summary>
    public string Key { get; }

    /// <summary>Why it was lost.</summary>
    public LockLossReason Reason { get; }
}
