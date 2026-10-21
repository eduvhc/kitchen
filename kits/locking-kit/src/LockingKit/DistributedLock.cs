using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using ResultingKit;

namespace LockingKit;

/// <summary>
/// LockingKit's engine: one per registered lock provider. It validates keys, waits with backoff, turns backend
/// outcomes into results, hands each acquired lease to a <see cref="LockHandle"/> that renews it, and records spans and
/// metrics. A backend only makes attempts.
/// </summary>
/// <remarks>
/// Every outcome of an acquisition is a <see cref="Result"/>: contention, outages, misconfiguration, a disposed
/// provider, a bad key. Only caller cancellation throws, as cooperative cancellation does everywhere in .NET.
/// </remarks>
internal sealed class DistributedLock(
    string name,
    string provider,
    ILockBackend backend,
    LockingOptions options,
    TimeProvider timeProvider,
    LockingKitMetrics metrics,
    Func<double>? random = null) : IDistributedLock, IAsyncDisposable, IDisposable
{
    private readonly ILockBackend _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    private readonly RetryBackoff _backoff = new(options.MinRetryDelay, options.MaxRetryDelay, random);
    private readonly ConcurrentDictionary<LockHandle, byte> _live = new();
    private int _disposed;

    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A name is required.", nameof(name)) : name;

    public string Provider { get; } = string.IsNullOrWhiteSpace(provider) ? throw new ArgumentException("A provider is required.", nameof(provider)) : provider;

    public LeaseTiming Timing { get; } = backend.Timing ?? throw new ArgumentException("The backend has no timing.", nameof(backend));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public TimeSpan ReleaseTimeout { get; } = options.ReleaseTimeout > TimeSpan.Zero
        ? options.ReleaseTimeout
        : throw new ArgumentOutOfRangeException(nameof(options), "ReleaseTimeout must be greater than zero.");

    internal LockingKitMetrics Metrics { get; } = metrics ?? throw new ArgumentNullException(nameof(metrics));

    internal WaitingRoom Room { get; } = new(timeProvider);

    internal ILockBackend Backend => _backend;

    internal int LiveHandles => _live.Count;

    public Task<Result<ILockHandle>> TryAcquireAsync(string key, CancellationToken ct = default) =>
        RunAsync(LockingKitTags.TryAcquire, key, TimeSpan.Zero, ct);

    public Task<Result<ILockHandle>> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct = default) =>
        RunAsync(LockingKitTags.Acquire, key, timeout, ct);

    // A container disposed synchronously still releases what this process holds.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Shutting down releases what this process still holds rather than leaving it to expire.
        await Task.WhenAll(_live.Keys.Select(h => h.DisposeAsync().AsTask())).ConfigureAwait(false);
        await _backend.DisposeAsync().ConfigureAwait(false);
    }

    internal void OnLost(LockHandle handle, LockLossReason reason)
    {
        Metrics.Lost(Name, Provider, reason);
        Metrics.Held(Name, Provider, -1);
        Metrics.HoldEnded(Name, Provider, TimeProvider.GetElapsedTime(handle.AcquiredAt));
        Room.Pulse(handle.Key);
    }

    internal void OnDisposed(LockHandle handle, bool wasHeld)
    {
        if (wasHeld)
        {
            Metrics.Held(Name, Provider, -1);
            Metrics.HoldEnded(Name, Provider, TimeProvider.GetElapsedTime(handle.AcquiredAt));
        }

        _live.TryRemove(handle, out _);
        Room.Pulse(handle.Key);
    }

    internal async Task ReleaseAsync(LockHandle handle)
    {
        using var activity = LockingKitInstrumentation.ActivitySource.StartActivity($"{LockingKitTags.Release} {Name}", ActivityKind.Client);
        Tag(activity, LockingKitTags.Release);

        var error = await ReleaseQuietlyAsync(handle.Lease).ConfigureAwait(false);

        if (activity is not null)
        {
            activity.SetTag(LockingKitTags.Outcome, error is null ? LockingKitTags.Success : LockingKitTags.Failure);

            if (error is not null)
            {
                activity.SetTag(LockingKitTags.ErrorType, error);
                activity.SetStatus(ActivityStatusCode.Error, error);
            }
        }

        Metrics.Released(Name, Provider, error);
    }

    private async Task<Result<ILockHandle>> RunAsync(string operation, string key, TimeSpan timeout, CancellationToken ct)
    {
        var start = TimeProvider.GetTimestamp();
        using var activity = LockingKitInstrumentation.ActivitySource.StartActivity($"{operation} {Name}", ActivityKind.Client);
        Tag(activity, operation);

        var attempts = 0;
        Result<ILockHandle> result;

        try
        {
            if (_disposed == 1)
            {
                result = LockingErrors.ProviderDisposed(Name);
            }
            else if (LockKeys.Validate(key) is { } keyError)
            {
                result = keyError;
            }
            else if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                result = LockingErrors.BadArgument("The timeout must be zero or more, or Timeout.InfiniteTimeSpan.");
            }
            else if (timeout == TimeSpan.Zero)
            {
                attempts = 1;
                result = await AttemptAsync(key, ct).ConfigureAwait(false);
            }
            else
            {
                result = await WaitAsync(key, timeout, start, () => attempts++, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity?.SetTag(LockingKitTags.Outcome, "cancelled");
            activity?.SetTag(LockingKitTags.Attempts, attempts);
            Metrics.Acquire(Name, Provider, operation, "cancelled", errorType: null, errorKind: null, TimeProvider.GetElapsedTime(start));
            throw;
        }

        Record(activity, operation, result, attempts, start);
        return result;
    }

    private async Task<Result<ILockHandle>> WaitAsync(string key, TimeSpan timeout, long start, Action countAttempt, CancellationToken ct)
    {
        var infinite = timeout == Timeout.InfiniteTimeSpan;

        TimeSpan Remaining()
        {
            if (infinite)
            {
                return Timeout.InfiniteTimeSpan;
            }

            var left = timeout - TimeProvider.GetElapsedTime(start);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        using var turn = await Room.WaitForTurnAsync(key, Remaining(), ct).ConfigureAwait(false);

        if (turn is null)
        {
            return LockingErrors.WaitTimedOut(key, timeout);
        }

        var retry = 0;

        while (true)
        {
            var released = turn.ReleaseSignal();
            countAttempt();

            var attempt = await AttemptAsync(key, ct).ConfigureAwait(false);

            if (attempt.IsSuccess || attempt.Error.Kind is not (ErrorKind.Conflict or ErrorKind.Transient))
            {
                return attempt;
            }

            var remaining = Remaining();

            if (remaining == TimeSpan.Zero)
            {
                // A backend that kept failing is the more useful answer than "still held".
                return attempt.Error.IsTransient ? attempt : LockingErrors.WaitTimedOut(key, timeout);
            }

            var pause = _backoff.Next(retry++);

            if (!infinite && pause > remaining)
            {
                pause = remaining;
            }

            await turn.PauseAsync(pause, released, ct).ConfigureAwait(false);
        }
    }

    private async Task<Result<ILockHandle>> AttemptAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Taken before the request leaves, so the lease's deadline is measured from no later than the backend's start.
        var requestedAt = TimeProvider.GetTimestamp();
        ILockLease? lease;

        try
        {
            lease = await _backend.TryAcquireAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ct.IsCancellationRequested && ex is OperationCanceledException))
        {
            return Translate(ex);
        }

        if (lease is null)
        {
            return LockingErrors.Held(key);
        }

        if (ct.IsCancellationRequested || _disposed == 1)
        {
            // Acquired, but nobody is left to own it: give it straight back.
            await ReleaseQuietlyAsync(lease).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return LockingErrors.ProviderDisposed(Name);
        }

        var handle = new LockHandle(this, key, lease, requestedAt);
        _live.TryAdd(handle, 0);
        Metrics.Held(Name, Provider, +1);
        handle.Start();
        return handle;
    }

    /// <summary>Releases and disposes a lease within <see cref="ReleaseTimeout"/>, whatever the backend does.</summary>
    /// <returns>The error type when the release failed, or <see langword="null"/>.</returns>
    private async Task<string?> ReleaseQuietlyAsync(ILockLease lease)
    {
        string? error = null;

        try
        {
            using var timeout = new CancellationTokenSource(ReleaseTimeout, TimeProvider);
            await lease.ReleaseAsync(timeout.Token).WaitAsync(ReleaseTimeout, TimeProvider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The lease expires anyway; a failed release only delays the next holder.
            error = ex.GetType().FullName ?? ex.GetType().Name;
        }

        try
        {
            await lease.DisposeAsync().AsTask().WaitAsync(ReleaseTimeout, TimeProvider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error ??= ex.GetType().FullName ?? ex.GetType().Name;
        }

        return error;
    }

    /// <summary>
    /// Every failed attempt becomes an error: the backend's classification first, then transport failures, and
    /// anything left is <see cref="LockingErrors.BackendFailed"/> rather than an exception.
    /// </summary>
    private Error Translate(Exception exception) =>
        _backend.TranslateException(exception)
        ?? TranslateTransportException(exception)
        ?? LockingErrors.Unexpected(exception);

    /// <summary>
    /// Failures below the provider SDK: timeouts and connection failures are transient. Unwraps
    /// <see cref="AggregateException"/>s from SDK retry loops.
    /// </summary>
    internal static Error? TranslateTransportException(Exception exception) => exception switch
    {
        AggregateException { InnerExceptions.Count: > 0 } aggregate =>
            TranslateTransportException(aggregate.Flatten().InnerExceptions[^1]),
        TimeoutException or OperationCanceledException => LockingErrors.BackendUnavailable($"timed out ({exception.Message})"),
        HttpRequestException or SocketException => LockingErrors.BackendUnavailable(exception.Message),
        IOException { InnerException: SocketException or HttpRequestException } => LockingErrors.BackendUnavailable(exception.Message),
        _ => null,
    };

    private void Tag(Activity? activity, string operation)
    {
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(LockingKitTags.Lock, Name);
            activity.SetTag(LockingKitTags.Provider, Provider);
            activity.SetTag(LockingKitTags.Operation, operation);
        }
    }

    private void Record(Activity? activity, string operation, Result<ILockHandle> result, int attempts, long start)
    {
        var duration = TimeProvider.GetElapsedTime(start);

        if (result.IsSuccess)
        {
            if (activity is not null)
            {
                activity.SetTag(LockingKitTags.Outcome, LockingKitTags.Acquired);
                activity.SetTag(LockingKitTags.Attempts, attempts);

                if (result.Value.FencingToken is { } token)
                {
                    activity.SetTag(LockingKitTags.FencingToken, token);
                }
            }

            Metrics.Acquire(Name, Provider, operation, LockingKitTags.Acquired, errorType: null, errorKind: null, duration);
            return;
        }

        var error = result.Error;
        var outcome = error.Code switch
        {
            LockingErrors.Busy => LockingKitTags.Busy,
            LockingErrors.Timeout => LockingKitTags.TimedOut,
            _ => LockingKitTags.Failure,
        };

        var failed = outcome == LockingKitTags.Failure;
        var kind = LockingKitTags.ToTagValue(error.Kind);

        if (activity is not null)
        {
            activity.SetTag(LockingKitTags.Outcome, outcome);
            activity.SetTag(LockingKitTags.Attempts, attempts);

            if (failed)
            {
                activity.SetTag(LockingKitTags.ErrorType, error.Code);
                activity.SetTag(LockingKitTags.ErrorKind, kind);

                // The code, not the message: messages name the key.
                activity.SetStatus(ActivityStatusCode.Error, error.Code);
            }
        }

        // Contention is an answer, not an error: busy and timeout carry no error attributes.
        Metrics.Acquire(Name, Provider, operation, outcome, failed ? error.Code : null, failed ? kind : null, duration);
    }
}
