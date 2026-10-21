namespace LockingKit;

/// <summary>
/// A held lock: the renewal scheduler, the loss signal and the release on dispose. All timing runs on the engine's
/// <see cref="TimeProvider"/>, so every schedule here is testable with a fake clock.
/// </summary>
/// <remarks>
/// <para>
/// The deadline is measured from the moment the last successful renewal was <em>sent</em>, never from when its answer
/// arrived: the backend started its lease no earlier than that, so the local deadline always falls before the
/// backend's expiry, short of a safety margin, whatever the network did in between.
/// </para>
/// <para>
/// A separate timer enforces the deadline. A renewal that hangs — a paused backend, a dead TCP connection — cannot
/// postpone the loss signal: the timer fires on time and cancels the renewal.
/// </para>
/// </remarks>
internal sealed class LockHandle(DistributedLock owner, string key, ILockLease lease, long requestedAt) : ILockHandle
{
    private const int Held = 0;
    private const int Lost = 1;
    private const int Disposed = 2;

    private readonly DistributedLock _owner = owner;
    private readonly LeaseTiming _timing = owner.Timing;
    private readonly TimeProvider _time = owner.TimeProvider;
    private readonly CancellationTokenSource _lost = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();

    // Created in Start: a timer callback needs the instance, which field initializers cannot reach.
    private ITimer _deadline = null!;
    private long _confirmedAt = requestedAt;
    private long _dueAt = long.MaxValue;
    private int _state;
    private int _disposeStarted;
    private LockLossReason? _reason;
    private Task _renewal = Task.CompletedTask;

    public string Key { get; } = key;

    public CancellationToken LostToken => _lost.Token;

    public bool IsLost => LossReason is not null;

    public LockLossReason? LossReason
    {
        get
        {
            lock (_gate)
            {
                return _reason;
            }
        }
    }

    public long? FencingToken => Lease.FencingToken;

    internal ILockLease Lease { get; } = lease;

    internal long AcquiredAt { get; } = requestedAt;

    public ResultingKit.Result EnsureHeld()
    {
        lock (_gate)
        {
            return _state == Held ? ResultingKit.Result.Success() : LockingErrors.NoLongerHeld(Key, _reason);
        }
    }

    public void ThrowIfLost()
    {
        lock (_gate)
        {
            if (_state == Lost)
            {
                throw new LockLostException(Key, _reason!.Value, _lost.Token);
            }

            ObjectDisposedException.ThrowIf(_state == Disposed, this);
        }
    }

    /// <summary>Arms the deadline and starts renewing. Called once, right after the lease was acquired.</summary>
    internal void Start()
    {
        _deadline = _time.CreateTimer(static state => ((LockHandle)state!).OnDeadline(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        lock (_gate)
        {
            if (_timing.GiveUpAfter is { } giveUp)
            {
                ArmDeadline(_confirmedAt, giveUp);
            }
        }

        _renewal = RenewLoopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
        {
            await _disposed.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _disposed.TrySetResult();
        }
    }

    public override string ToString() => $"Lock '{Key}' ({(IsLost ? $"lost: {LossReason}" : "held")})";

    private async Task DisposeCoreAsync()
    {
        bool wasHeld;

        lock (_gate)
        {
            wasHeld = _state == Held;
            _state = Disposed;
            _deadline.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        // The protected work stops first, then renewal, then the release goes out: never a renewal after it.
        Signal(_lost);
        await _stop.CancelAsync().ConfigureAwait(false);

        try
        {
            await _renewal.WaitAsync(_owner.ReleaseTimeout, _time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A backend that ignores cancellation; the release below still goes out, and expiry backs it up.
        }
        catch (Exception)
        {
            // The renewal loop never faults on purpose; disposing must not surface whatever a cancelled renewal threw.
        }

        await _owner.ReleaseAsync(this).ConfigureAwait(false);
        await _deadline.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
        _owner.OnDisposed(this, wasHeld);
    }

    private async Task RenewLoopAsync()
    {
        var stop = _stop.Token;
        var failures = 0;

        try
        {
            while (true)
            {
                var wait = failures == 0 ? _timing.RenewalInterval - _time.GetElapsedTime(_confirmedAt) : _timing.RetryInterval;

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _time, stop).ConfigureAwait(false);
                }

                var started = _time.GetTimestamp();
                TimeSpan budget;

                lock (_gate)
                {
                    if (_state != Held)
                    {
                        return;
                    }

                    if (_timing.GiveUpAfter is { } giveUp)
                    {
                        budget = giveUp - _time.GetElapsedTime(_confirmedAt);
                    }
                    else
                    {
                        // A connection-held lock has no expiry to wait for: a check that outlasts its interval is a loss.
                        budget = _timing.RenewalInterval;
                        ArmDeadline(started, budget);
                    }
                }

                if (budget <= TimeSpan.Zero)
                {
                    MarkLost(LockLossReason.Expired);
                    return;
                }

                bool held;

                using (var attempt = new CancellationTokenSource(budget, _time))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, attempt.Token))
                {
                    try
                    {
                        held = await Lease.RenewAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Stopped mid-renewal (lost or disposed): an SDK may report the cancellation as any exception.
                        if (stop.IsCancellationRequested)
                        {
                            return;
                        }

                        _owner.Metrics.Renewal(_owner.Name, _owner.Provider, success: false);

                        if (_timing.Duration is null)
                        {
                            MarkLost(LockLossReason.ConnectionLost);
                            return;
                        }

                        // Unknown outcome: retry until the deadline timer gives up.
                        failures++;
                        continue;
                    }
                }

                if (stop.IsCancellationRequested)
                {
                    return;
                }

                if (!held)
                {
                    _owner.Metrics.Renewal(_owner.Name, _owner.Provider, success: false);
                    MarkLost(LockLossReason.Revoked);
                    return;
                }

                lock (_gate)
                {
                    if (_state != Held)
                    {
                        return;
                    }

                    _confirmedAt = started;
                    failures = 0;

                    if (_timing.GiveUpAfter is { } giveUp)
                    {
                        ArmDeadline(started, giveUp);
                    }
                    else
                    {
                        DisarmDeadline();
                    }
                }

                _owner.Metrics.Renewal(_owner.Name, _owner.Provider, success: true);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    // Callers hold _gate.
    private void ArmDeadline(long from, TimeSpan after)
    {
        var due = after - _time.GetElapsedTime(from);
        _dueAt = from + (long)(after.Ticks * ((double)_time.TimestampFrequency / TimeSpan.TicksPerSecond));
        _deadline.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    // Callers hold _gate.
    private void DisarmDeadline()
    {
        _dueAt = long.MaxValue;
        _deadline.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnDeadline()
    {
        lock (_gate)
        {
            if (_state != Held || _dueAt == long.MaxValue)
            {
                return;
            }

            var now = _time.GetTimestamp();

            // Fired before its time: a renewal re-armed it meanwhile, or the system timer ran a little ahead of the
            // monotonic clock. Either way, wait out the rest rather than give up the deadline.
            if (now < _dueAt)
            {
                var rest = _time.GetElapsedTime(now, _dueAt);
                _deadline.Change(rest > TimeSpan.FromMilliseconds(1) ? rest : TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
                return;
            }
        }

        MarkLost(_timing.Duration is null ? LockLossReason.ConnectionLost : LockLossReason.Expired);
    }

    private void MarkLost(LockLossReason reason)
    {
        lock (_gate)
        {
            if (_state != Held)
            {
                return;
            }

            _state = Lost;
            _reason = reason;
            DisarmDeadline();
        }

        _owner.OnLost(this, reason);
        Signal(_lost);

        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void Signal(CancellationTokenSource source)
    {
        if (source.IsCancellationRequested)
        {
            return;
        }

        // Callbacks run on the thread pool, never inside the renewal loop or the deadline timer; a throwing callback
        // must not break either.
        _ = source.CancelAsync().ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
