namespace LockingKit;

/// <summary>
/// The pause between attempts while waiting for a lock: capped exponential growth with equal jitter. The cap is what
/// keeps waiting fair across processes — a waiter that has waited a minute polls exactly as often as one that just
/// arrived, so it is not starved by newcomers — and the jitter keeps contenders from retrying in lockstep.
/// </summary>
internal sealed class RetryBackoff
{
    private readonly long _min;
    private readonly long _max;
    private readonly Func<double> _random;

    public RetryBackoff(TimeSpan min, TimeSpan max, Func<double>? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(min, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);

        _min = min.Ticks;
        _max = max.Ticks;
        _random = random ?? Random.Shared.NextDouble;
    }

    /// <summary>The pause before attempt <paramref name="retry"/> + 1, where 0 is the first retry.</summary>
    /// <returns>A delay in [ceiling / 2, ceiling], ceiling = min(max, min × 2^retry).</returns>
    public TimeSpan Next(int retry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retry);

        // Doubling past the cap only risks overflow.
        var ceiling = retry >= 62 || _min > _max >> retry ? _max : Math.Min(_max, _min << retry);
        var half = ceiling / 2;
        var jitter = (long)(Math.Clamp(_random(), 0, 1) * (ceiling - half));

        return TimeSpan.FromTicks(half + jitter);
    }
}
