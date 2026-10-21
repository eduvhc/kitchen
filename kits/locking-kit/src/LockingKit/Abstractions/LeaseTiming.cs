namespace LockingKit;

/// <summary>
/// When the engine renews a lease and when it declares it lost. Two shapes cover the backends: a lease that expires
/// unless renewed (Azure blob leases), and a lock held for as long as a connection lives (PostgreSQL advisory locks).
/// </summary>
public sealed record LeaseTiming
{
    private LeaseTiming(TimeSpan? duration, TimeSpan renewalInterval, TimeSpan safetyMargin)
    {
        Duration = duration;
        RenewalInterval = renewalInterval;
        SafetyMargin = safetyMargin;
    }

    /// <summary>
    /// How long the backend keeps the lock after a renewal reaches it; <see langword="null"/> for a connection-held
    /// lock, which has no expiry.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>How often the engine renews or confirms the lock.</summary>
    public TimeSpan RenewalInterval { get; }

    /// <summary>
    /// How long before the backend's expiry the handle declares the lock lost, to absorb clock-rate drift and the
    /// time a loss takes to reach the work. Zero for a connection-held lock.
    /// </summary>
    public TimeSpan SafetyMargin { get; }

    /// <summary>The deadline after the start of the last successful renewal, or <see langword="null"/> when there is none.</summary>
    public TimeSpan? GiveUpAfter => Duration - SafetyMargin;

    /// <summary>How long the engine waits before retrying a renewal that failed without an answer.</summary>
    public TimeSpan RetryInterval => TimeSpan.FromTicks(Math.Max(RenewalInterval.Ticks / 4, TimeSpan.TicksPerMillisecond * 10));

    /// <summary>
    /// A lease that expires <paramref name="duration"/> after each renewal. The engine renews every third of it and
    /// gives up a tenth of it before expiry, so two renewals can fail before the lock is lost.
    /// </summary>
    /// <param name="duration">The lease duration the backend enforces.</param>
    public static LeaseTiming Expiring(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.FromMilliseconds(30));
        return new LeaseTiming(duration, duration / 3, duration / 10);
    }

    /// <summary>
    /// A lock held for as long as a connection lives. The engine confirms it every <paramref name="checkInterval"/>,
    /// and declares it lost when a check fails or takes longer than the interval.
    /// </summary>
    /// <param name="checkInterval">How often to confirm the lock.</param>
    public static LeaseTiming ConnectionHeld(TimeSpan checkInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(checkInterval, TimeSpan.FromMilliseconds(10));
        return new LeaseTiming(null, checkInterval, TimeSpan.Zero);
    }
}
