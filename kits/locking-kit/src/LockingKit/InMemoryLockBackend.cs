using ResultingKit;

namespace LockingKit;

/// <summary>
/// Leases in a dictionary. It models a real lease backend — leases expire on the <see cref="TimeProvider"/> unless
/// renewed, an expired lease can be taken by anyone, and every acquisition gets a larger fencing token — so code and
/// tests that run against it see the same behaviour as against Azure, within one process.
/// </summary>
internal sealed class InMemoryLockBackend(TimeSpan leaseDuration, TimeProvider timeProvider) : ILockBackend
{
    private readonly Dictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly long _durationTicks = (long)(leaseDuration.Ticks * ((double)timeProvider.TimestampFrequency / TimeSpan.TicksPerSecond));
    private long _fence;

    public LeaseTiming Timing { get; } = LeaseTiming.Expiring(leaseDuration);

    /// <summary>The number of keys with a live (unexpired) lease.</summary>
    internal int HeldCount
    {
        get
        {
            lock (_leases)
            {
                var now = _time.GetTimestamp();
                return _leases.Values.Count(l => l.ExpiresAt > now);
            }
        }
    }

    public Task<ILockLease?> TryAcquireAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_leases)
        {
            var now = _time.GetTimestamp();

            if (_leases.TryGetValue(key, out var current) && current.ExpiresAt > now)
            {
                return Task.FromResult<ILockLease?>(null);
            }

            var lease = new Lease(this, key, ++_fence, now + _durationTicks);
            _leases[key] = lease;
            return Task.FromResult<ILockLease?>(lease);
        }
    }

    public Error? TranslateException(Exception exception) => null;

    // The table outlives any one registration that shares it.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class Lease(InMemoryLockBackend backend, string key, long fence, long expiresAt) : ILockLease
    {
        public long ExpiresAt { get; private set; } = expiresAt;

        public long? FencingToken { get; } = fence;

        public Task<bool> RenewAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            lock (backend._leases)
            {
                // Like Azure, an expired lease can still be renewed as long as nobody else has taken the key since.
                if (!backend._leases.TryGetValue(key, out var current) || !ReferenceEquals(current, this))
                {
                    return Task.FromResult(false);
                }

                ExpiresAt = backend._time.GetTimestamp() + backend._durationTicks;
                return Task.FromResult(true);
            }
        }

        public Task ReleaseAsync(CancellationToken ct)
        {
            lock (backend._leases)
            {
                if (backend._leases.TryGetValue(key, out var current) && ReferenceEquals(current, this))
                {
                    backend._leases.Remove(key);
                }
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
