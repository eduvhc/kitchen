namespace LockingKit.UnitTests;

/// <summary>
/// A clock whose timers fire a little before they are due, as the system timer can relative to the monotonic
/// clock. Each timer runs 10% early, at most 5 ms.
/// </summary>
internal sealed class EarlyTimers(TimeProvider inner) : TimeProvider
{
    public override long TimestampFrequency => inner.TimestampFrequency;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new Timer(inner.CreateTimer(callback, state, Early(dueTime), period));

    private static TimeSpan Early(TimeSpan due)
    {
        if (due <= TimeSpan.Zero)
        {
            return due;
        }

        var early = TimeSpan.FromTicks(Math.Min(due.Ticks / 10, TimeSpan.FromMilliseconds(5).Ticks));
        return due - early;
    }

    private sealed class Timer(ITimer inner) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(Early(dueTime), period);

        public void Dispose() => inner.Dispose();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
