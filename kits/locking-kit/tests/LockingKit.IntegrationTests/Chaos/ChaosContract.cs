using System.Diagnostics;
using LockingKit.Postgres;

namespace LockingKit.IntegrationTests;

/// <summary>
/// Faults every provider must survive: a holder that freezes, disposal racing a renewal, and a lock lost in the middle
/// of the work it protects. Faults are injected into the holder's renewals, never into the backend, so the backend
/// behaves exactly as it would for a real frozen process.
/// </summary>
public abstract class ChaosContract
{
    private string _prefix = null!;

    public TestContext TestContext { get; set; } = null!;

    internal abstract ProviderUnderTest Provider { get; }

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestInitialize]
    public void UseFreshKeys() => _prefix = $"chaos/{Guid.NewGuid():N}/";

    [TestCleanup]
    public Task StopProcessesAsync() => Provider.StopProcessesAsync();

    [TestMethod]
    public async Task A_frozen_holder_loses_its_lock_before_anyone_else_can_take_it()
    {
        FaultyBackend faulty = null!;
        var a = Provider.NewProcess(inner => faulty = new FaultyBackend(inner));
        var b = Provider.NewProcess();
        var key = _prefix + "frozen";
        var clock = Stopwatch.StartNew();

        var handle = (await a.TryAcquireAsync(key, Ct)).Value;
        var lostAt = TimeSpan.MaxValue;
        handle.LostToken.Register(() => lostAt = clock.Elapsed);
        faulty.Fault = RenewalFault.Freeze;

        if (Provider.Expires)
        {
            // Nobody releases: the backend's lease has to expire before B can get in.
            var taken = await b.AcquireAsync(key, Provider.Period * 2 + TimeSpan.FromSeconds(10), Ct);
            var takenAt = clock.Elapsed;

            Assert.IsTrue(taken.IsSuccess, taken.ToString());
            Assert.AreEqual(LockLossReason.Expired, handle.LossReason);
            Assert.IsLessThan(takenAt, lostAt, $"The frozen holder learned of its loss at {lostAt}, after B took the lock at {takenAt}.");
            Assert.IsGreaterThanOrEqualTo(Provider.Period - TimeSpan.FromSeconds(0.5), takenAt, "B got in before the lease could have expired.");
            Assert.IsLessThanOrEqualTo(Provider.Period, lostAt, "The holder gives up before the backend's expiry.");

            await taken.Value.DisposeAsync();
        }
        else
        {
            // A connection-held lock does not expire: the holder cannot confirm it and gives up, but the server keeps
            // it for the frozen process — conservative in the safe direction — until the connection closes.
            await WaitUntilAsync(() => handle.IsLost, Provider.Period * 3 + TimeSpan.FromSeconds(3), "the holder gave up");
            Assert.AreEqual(LockLossReason.ConnectionLost, handle.LossReason);

            for (var i = 0; i < 4; i++)
            {
                Assert.AreEqual(LockingErrors.Busy, (await b.TryAcquireAsync(key, Ct)).Error?.Code);
                await Task.Delay(Provider.Period, Ct);
            }
        }

        await handle.DisposeAsync();
        await using var next = (await b.AcquireAsync(key, TimeSpan.FromSeconds(10), Ct)).Value;
    }

    [TestMethod]
    public async Task Disposal_racing_a_renewal_never_extends_a_released_lease()
    {
        var interval = Provider.Expires ? Provider.Period / 3 : Provider.Period;
        var slow = interval / 3;
        var rounds = Provider.Period >= TimeSpan.FromSeconds(10) ? 3 : 8;
        var b = Provider.NewProcess();

        for (var round = 0; round < rounds; round++)
        {
            FaultyBackend faulty = null!;
            var a = Provider.NewProcess(inner => faulty = new FaultyBackend(inner) { Fault = RenewalFault.Slow, SlowBy = slow });
            var key = $"{_prefix}race-{round}";

            var handle = (await a.TryAcquireAsync(key, Ct)).Value;

            // Dispose while the first renewal is in flight.
            await Task.Delay(interval + slow / 2, Ct);
            await handle.DisposeAsync();

            Assert.AreEqual(1, faulty.RenewalsStarted, $"Round {round}: dispose did not land during the renewal.");
            Assert.AreEqual(0, faulty.RenewalsAfterRelease, $"Round {round}: a renewal reached the backend after the release.");

            // Free at once: released, and not quietly re-extended by the renewal it raced.
            var next = await b.TryAcquireAsync(key, Ct);
            Assert.IsTrue(next.IsSuccess, $"Round {round}: {next}");
            await next.Value.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task A_lock_lost_mid_work_stops_the_work_and_its_fencing_token_turns_away_the_stale_holder()
    {
        FaultyBackend faulty = null!;
        var a = Provider.NewProcess(inner => faulty = new FaultyBackend(inner));
        var b = Provider.NewProcess();
        var key = _prefix + "fenced-work";
        var resource = new FencedResource();
        var clock = Stopwatch.StartNew();

        var handle = (await a.TryAcquireAsync(key, Ct)).Value;
        var tokenA = handle.FencingToken!.Value;
        var workEndedAt = TimeSpan.MaxValue;

        var work = Task.Run(async () =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct, handle.LostToken);

            try
            {
                while (true)
                {
                    handle.ThrowIfLost();

                    // Refused once the successor has written: the fence holds even before A learns of its loss.
                    resource.TryWrite(tokenA, "a");
                    await Task.Delay(50, stop.Token);
                }
            }
            finally
            {
                workEndedAt = clock.Elapsed;
            }
        }, Ct);

        await Task.Delay(300, Ct);
        await InduceLossAsync(faulty, handle);

        var taken = await b.AcquireAsync(key, Provider.Period * 2 + TimeSpan.FromSeconds(10), Ct);
        var takenAt = clock.Elapsed;
        Assert.IsTrue(taken.IsSuccess, taken.ToString());
        await using var successor = taken.Value;
        var tokenB = successor.FencingToken!.Value;

        Assert.IsGreaterThan(tokenA, tokenB);
        Assert.IsTrue(resource.TryWrite(tokenB, "b"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => work.WaitAsync(Provider.Period * 2 + TimeSpan.FromSeconds(5), Ct));
        Assert.IsTrue(handle.IsLost);

        if (Provider.Expires)
        {
            Assert.IsLessThan(takenAt, workEndedAt, "With a lease, the work stops before anyone else can start.");
        }

        // A holder that paused past its loss and writes late is refused by the fence, whatever the timing.
        Assert.IsFalse(resource.TryWrite(tokenA, "late a"));
        Assert.AreEqual("b", resource.Writes[^1]);

        await handle.DisposeAsync();
    }

    /// <summary>Makes the holder of <paramref name="handle"/> lose its lock; a frozen holder by default.</summary>
    internal virtual Task InduceLossAsync(FaultyBackend faulty, ILockHandle handle)
    {
        faulty.Fault = RenewalFault.Freeze;
        return Task.CompletedTask;
    }

    protected static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var watch = Stopwatch.StartNew();

        while (!condition())
        {
            if (watch.Elapsed > timeout)
            {
                Assert.Fail($"Timed out after {timeout} waiting until {because}.");
            }

            await Task.Delay(20);
        }
    }
}

[TestClass]
public sealed class InMemoryChaosTests : ChaosContract
{
    private static readonly InMemoryUnderTest Shared = new();

    internal override ProviderUnderTest Provider => Shared;
}

[TestClass]
public sealed class AzureBlobLeaseChaosTests : ChaosContract
{
    private static readonly AzureUnderTest Shared = new(() => TestHost.Azurite.ConnectionString);

    internal override ProviderUnderTest Provider => Shared;
}

[TestClass]
public sealed class PostgresAdvisoryLockChaosTests : ChaosContract
{
    private static readonly PostgresUnderTest Shared = new(() => TestHost.Postgres.ConnectionString);

    internal override ProviderUnderTest Provider => Shared;

    // A crashed or partitioned holder, as the server sees it: its session is killed and the lock freed at once.
    internal override Task InduceLossAsync(FaultyBackend faulty, ILockHandle handle)
    {
        var lease = (PostgresAdvisoryLease)FaultyBackend.Unwrap(handle);
        return TestHost.Postgres.ExecuteSqlAsync($"SELECT pg_terminate_backend({lease.ProcessId})", TestContext.CancellationToken);
    }
}
