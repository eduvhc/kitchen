using ResultingKit;

namespace LockingKit.IntegrationTests;

/// <summary>How a <see cref="FaultyBackend"/> treats renewals.</summary>
internal enum RenewalFault
{
    /// <summary>Renewals pass through.</summary>
    None,

    /// <summary>Renewals never reach the backend and never return: the holder has frozen.</summary>
    Freeze,

    /// <summary>Renewals reach the backend only after a pause, so they are in flight for a while.</summary>
    Slow,
}

/// <summary>Wraps a real backend and injects faults into renewals, the way a frozen or struggling process would.</summary>
internal sealed class FaultyBackend(ILockBackend inner) : ILockBackend
{
    private volatile RenewalFault _fault;

    public LeaseTiming Timing => inner.Timing;

    public RenewalFault Fault
    {
        get => _fault;
        set => _fault = value;
    }

    public TimeSpan SlowBy { get; set; } = TimeSpan.FromMilliseconds(500);

    public int RenewalsStarted;

    public int RenewalsAfterRelease;

    public async Task<ILockLease?> TryAcquireAsync(string key, CancellationToken ct) =>
        await inner.TryAcquireAsync(key, ct) is { } lease ? new Lease(this, lease) : null;

    public Error? TranslateException(Exception exception) => inner.TranslateException(exception);

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    /// <summary>The real lease behind a handle, through any wrapping.</summary>
    public static ILockLease Unwrap(ILockHandle handle)
    {
        var lease = ((LockHandle)handle).Lease;
        return lease is Lease wrapped ? wrapped.Inner : lease;
    }

    private sealed class Lease(FaultyBackend backend, ILockLease inner) : ILockLease
    {
        private volatile bool _released;

        public ILockLease Inner => inner;

        public long? FencingToken => inner.FencingToken;

        public async Task<bool> RenewAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref backend.RenewalsStarted);

            switch (backend.Fault)
            {
                case RenewalFault.Freeze:
                    // A frozen process does not answer cancellation either.
                    await new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
                    break;

                case RenewalFault.Slow:
                    await Task.Delay(backend.SlowBy, ct);
                    break;
            }

            if (_released)
            {
                Interlocked.Increment(ref backend.RenewalsAfterRelease);
            }

            return await inner.RenewAsync(ct);
        }

        public Task ReleaseAsync(CancellationToken ct)
        {
            _released = true;
            return inner.ReleaseAsync(ct);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
