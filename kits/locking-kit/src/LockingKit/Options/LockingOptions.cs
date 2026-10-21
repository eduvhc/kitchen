namespace LockingKit;

/// <summary>
/// Engine settings every provider's options share. Bound from <c>Locking:{name}</c> together with the provider's own
/// settings.
/// </summary>
public abstract class LockingOptions
{
    /// <summary>
    /// The shortest pause between attempts while waiting in <see cref="IDistributedLock.AcquireAsync"/>. Defaults to
    /// 50 ms.
    /// </summary>
    public TimeSpan MinRetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The longest pause between attempts while waiting. The pause doubles from <see cref="MinRetryDelay"/> up to this
    /// cap, with jitter, so a waiter never polls less often than this however long it has waited. Defaults to 1 s.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long disposing a handle waits for the backend to confirm the release. A release that does not finish in
    /// time is abandoned; the lease expires on its own. Defaults to 5 s.
    /// </summary>
    public TimeSpan ReleaseTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Checks the engine settings; providers call it from their own options validation.</summary>
    /// <returns>A description of the first problem, or <see langword="null"/> when the settings are valid.</returns>
    public string? ValidateEngineSettings()
    {
        if (MinRetryDelay <= TimeSpan.Zero)
        {
            return "MinRetryDelay must be greater than zero.";
        }

        if (MaxRetryDelay < MinRetryDelay)
        {
            return "MaxRetryDelay must be at least MinRetryDelay.";
        }

        if (ReleaseTimeout <= TimeSpan.Zero)
        {
            return "ReleaseTimeout must be greater than zero.";
        }

        return null;
    }
}
