using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LockingKit.UnitTests;

internal sealed class TestLockingOptions : LockingOptions;

/// <summary>Builds engines over test backends, and drives a fake clock the way the engine's loops need it driven.</summary>
internal static class Engines
{
    private static readonly IMeterFactory Meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    public static DistributedLock Create(
        ILockBackend backend,
        TimeProvider time,
        LockingOptions? options = null,
        Func<double>? random = null,
        IMeterFactory? meters = null) =>
        new("Default", "scripted", backend, options ?? new TestLockingOptions(), time, new LockingKitMetrics(meters ?? Meters), random);

    public static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.Zero };

    /// <summary>Lets continuations scheduled by the last clock move run.</summary>
    public static Task SettleAsync() => Task.Delay(30);

    /// <summary>Moves the clock in steps, settling after each, so loops waiting on it keep up.</summary>
    public static async Task AdvanceAsync(this FakeTimeProvider time, TimeSpan by, TimeSpan? step = null)
    {
        var size = step ?? TimeSpan.FromSeconds(1);
        var moved = TimeSpan.Zero;

        while (moved < by)
        {
            var next = by - moved < size ? by - moved : size;
            time.Advance(next);
            moved += next;
            await SettleAsync();
        }
    }

    /// <summary>Waits in real time until <paramref name="condition"/> holds.</summary>
    public static async Task UntilAsync(Func<bool> condition, string because, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(5);
        }
    }

    public static async Task<ILockHandle> HoldAsync(this IDistributedLock locks, string key = "jobs/nightly")
    {
        var result = await locks.TryAcquireAsync(key);
        Assert.IsTrue(result.IsSuccess, result.ToString());
        return result.Value;
    }
}
