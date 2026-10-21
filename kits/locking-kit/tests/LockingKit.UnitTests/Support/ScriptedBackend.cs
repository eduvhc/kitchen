using System.Collections.Concurrent;
using ResultingKit;

namespace LockingKit.UnitTests;

/// <summary>
/// A backend whose every answer the test decides, and which logs what the engine asked of it and when, in order.
/// </summary>
internal sealed class ScriptedBackend(LeaseTiming timing) : ILockBackend
{
    private int _attempts;
    private int _renewals;
    private int _releases;
    private long _fence;

    public LeaseTiming Timing { get; } = timing;

    public ConcurrentQueue<string> Log { get; } = new();

    /// <summary>Answers each attempt; the default grants a fresh lease.</summary>
    public Func<string, CancellationToken, Task<ILockLease?>>? OnAcquire { get; set; }

    /// <summary>Answers each renewal; the default succeeds.</summary>
    public Func<CancellationToken, Task<bool>> OnRenew { get; set; } = _ => Task.FromResult(true);

    /// <summary>Answers each release; the default succeeds.</summary>
    public Func<CancellationToken, Task> OnRelease { get; set; } = _ => Task.CompletedTask;

    public Func<Exception, Error?> OnTranslate { get; set; } = _ => null;

    public int Attempts => Volatile.Read(ref _attempts);

    public int Renewals => Volatile.Read(ref _renewals);

    public int Releases => Volatile.Read(ref _releases);

    public bool Disposed { get; private set; }

    public static ScriptedBackend Expiring(TimeSpan duration) => new(LeaseTiming.Expiring(duration));

    public static ScriptedBackend ConnectionHeld(TimeSpan interval) => new(LeaseTiming.ConnectionHeld(interval));

    public Task<ILockLease?> TryAcquireAsync(string key, CancellationToken ct)
    {
        Interlocked.Increment(ref _attempts);
        Log.Enqueue($"acquire:{key}");

        return OnAcquire is { } onAcquire ? onAcquire(key, ct) : Task.FromResult<ILockLease?>(Grant());
    }

    public ScriptedLease Grant() => new(this, Interlocked.Increment(ref _fence));

    public Error? TranslateException(Exception exception) => OnTranslate(exception);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    internal sealed class ScriptedLease(ScriptedBackend backend, long fence) : ILockLease
    {
        public long? FencingToken { get; } = fence;

        public bool Disposed { get; private set; }

        public async Task<bool> RenewAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref backend._renewals);
            backend.Log.Enqueue("renew:start");

            try
            {
                var held = await backend.OnRenew(ct);
                backend.Log.Enqueue(held ? "renew:held" : "renew:revoked");
                return held;
            }
            catch (OperationCanceledException)
            {
                backend.Log.Enqueue("renew:cancelled");
                throw;
            }
            catch
            {
                backend.Log.Enqueue("renew:failed");
                throw;
            }
        }

        public async Task ReleaseAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref backend._releases);
            backend.Log.Enqueue("release");
            await backend.OnRelease(ct);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
