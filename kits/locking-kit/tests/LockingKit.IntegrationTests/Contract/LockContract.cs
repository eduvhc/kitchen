using System.Diagnostics;
using ResultingKit;

namespace LockingKit.IntegrationTests;

/// <summary>
/// The behaviour every provider owes <see cref="IDistributedLock"/> callers, run against each real backend. "Processes"
/// are separate engines with their own clients, so contention here is contention between hosts. A test that passes
/// for one provider and fails for another is a contract bug, not a provider quirk.
/// </summary>
public abstract class LockContract
{
    private string _prefix = null!;

    public TestContext TestContext { get; set; } = null!;

    internal abstract ProviderUnderTest Provider { get; }

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestInitialize]
    public void UseFreshKeys() => _prefix = $"contract/{Guid.NewGuid():N}/";

    [TestCleanup]
    public Task StopProcessesAsync() => Provider.StopProcessesAsync();

    [TestMethod]
    public async Task A_free_key_is_acquired_and_then_busy_for_everyone_including_its_own_process()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("orders/42");

        var held = await a.TryAcquireAsync(key, Ct);
        Assert.IsTrue(held.IsSuccess, held.ToString());

        var sameProcess = await a.TryAcquireAsync(key, Ct);
        var otherProcess = await b.TryAcquireAsync(key, Ct);

        Assert.AreEqual(LockingErrors.Busy, sameProcess.Error?.Code, "Locks are not reentrant.");
        Assert.AreEqual(LockingErrors.Busy, otherProcess.Error?.Code);
        Assert.AreEqual(ErrorKind.Conflict, otherProcess.Error?.Kind);

        await held.Value.DisposeAsync();
    }

    [TestMethod]
    public async Task Disposing_releases_at_once_so_the_next_holder_does_not_wait_for_expiry()
    {
        var processes = new[] { Provider.NewProcess(), Provider.NewProcess() };
        var key = Key("jobs/nightly");

        for (var round = 0; round < 6; round++)
        {
            // Try, not wait: the previous holder's release must already be visible.
            var result = await processes[round % 2].TryAcquireAsync(key, Ct);
            Assert.IsTrue(result.IsSuccess, $"Round {round}: {result}");
            await result.Value.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Disposing_twice_is_harmless_and_ends_protection_without_reporting_a_loss()
    {
        var locks = Provider.NewProcess();
        var handle = await Hold(locks, Key("k"));

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        Assert.IsTrue(handle.LostToken.IsCancellationRequested);
        Assert.IsFalse(handle.IsLost);
        Assert.IsNull(handle.LossReason);
        Assert.ThrowsExactly<ObjectDisposedException>(handle.ThrowIfLost);
        await using var next = await Hold(locks, Key("k"));
    }

    [TestMethod]
    public async Task Waiting_acquires_as_soon_as_the_holder_releases()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("k");
        var held = await Hold(a, key);

        var watch = Stopwatch.StartNew();
        var waiting = b.AcquireAsync(key, TimeSpan.FromSeconds(30), Ct);
        await Task.Delay(1000, Ct);
        Assert.IsFalse(waiting.IsCompleted);

        await held.DisposeAsync();
        await using var handle = (await waiting).Value;

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.9), watch.Elapsed);
        Assert.IsLessThan(TimeSpan.FromSeconds(4), watch.Elapsed, "Picked up within a few retry pauses of the release.");
    }

    [TestMethod]
    public async Task Waiting_on_a_lock_that_stays_held_ends_in_a_timeout_result()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("k");
        await using var held = await Hold(a, key);

        var watch = Stopwatch.StartNew();
        var result = await b.AcquireAsync(key, TimeSpan.FromSeconds(1.5), Ct);

        Assert.AreEqual(LockingErrors.Timeout, result.Error?.Code);
        Assert.AreEqual(ErrorKind.Conflict, result.Error?.Kind);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.4), watch.Elapsed);
        Assert.IsLessThan(TimeSpan.FromSeconds(6), watch.Elapsed);
    }

    [TestMethod]
    public async Task Many_contenders_across_processes_never_overlap()
    {
        const int Processes = 4;
        const int ContendersPerProcess = 5;
        const int Rounds = 4;

        var key = Key("hot");
        var checker = new ExclusionChecker();
        var engines = Enumerable.Range(0, Processes).Select(_ => Provider.NewProcess()).ToArray();

        var contenders = engines.SelectMany((engine, p) => Enumerable.Range(0, ContendersPerProcess).Select(c => Task.Run(async () =>
        {
            for (var round = 0; round < Rounds; round++)
            {
                var result = await engine.AcquireAsync(key, TimeSpan.FromMinutes(2), Ct);
                Assert.IsTrue(result.IsSuccess, result.ToString());

                await using var handle = result.Value;
                await checker.RunAsync($"p{p}c{c}r{round}", async () =>
                {
                    handle.ThrowIfLost();
                    await Task.Delay(Random.Shared.Next(1, 15), Ct);
                    handle.ThrowIfLost();
                });
            }
        }, Ct))).ToArray();

        await Task.WhenAll(contenders);

        checker.AssertExclusive(Processes * ContendersPerProcess * Rounds);
    }

    [TestMethod]
    public async Task Different_keys_never_contend()
    {
        var locks = Provider.NewProcess();
        var handles = new List<ILockHandle>();

        for (var i = 0; i < 10; i++)
        {
            handles.Add(await Hold(locks, Key($"shard-{i}")));
        }

        foreach (var handle in handles)
        {
            await handle.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Any_string_is_a_key_and_lookalike_keys_are_distinct_locks()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var keys = new[]
        {
            "a/b", "a~2Fb", "a%2Fb", "a//b", "/a/b", "a/b/", "A/b", "a/b ", "a/b.", "a/./b", "a/../b", ".", "..",
            "tenant 7/report (final).pdf", "café", "🔒/🔑", "tab\there", @"back\slash",

            // As long as a key may be, prefix included: plain, and escaped past Azure's name limit.
            new string('x', DistributedLocks.MaxKeyLength - _prefix.Length),
            new string('é', DistributedLocks.MaxKeyLength - _prefix.Length),
        };

        var handles = new List<ILockHandle>();

        foreach (var key in keys)
        {
            handles.Add(await Hold(a, _prefix + key));
        }

        foreach (var key in keys)
        {
            var other = await b.TryAcquireAsync(_prefix + key, Ct);
            Assert.AreEqual(LockingErrors.Busy, other.Error?.Code, key);
        }

        foreach (var handle in handles)
        {
            await handle.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Bad_keys_are_validation_results_before_any_request()
    {
        var locks = Provider.NewProcess();

        Assert.AreEqual(LockingErrors.InvalidKey, (await locks.TryAcquireAsync(string.Empty, Ct)).Error?.Code);
        Assert.AreEqual(LockingErrors.InvalidKey, (await locks.TryAcquireAsync(new string('x', DistributedLocks.MaxKeyLength + 1), Ct)).Error?.Code);
    }

    [TestMethod]
    public async Task Cancelling_a_wait_throws_and_leaves_the_holder_undisturbed()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("k");
        await using var held = await Hold(a, key);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAsync<OperationCanceledException>(() => b.AcquireAsync(key, TimeSpan.FromMinutes(1), cts.Token));

        Assert.IsFalse(held.LostToken.IsCancellationRequested);
        held.ThrowIfLost();
        Assert.AreEqual(LockingErrors.Busy, (await b.TryAcquireAsync(key, Ct)).Error?.Code, "The holder still holds it.");
    }

    [TestMethod]
    public async Task A_cancelled_token_throws_without_taking_the_lock()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("k");

        await Assert.ThrowsAsync<OperationCanceledException>(() => a.TryAcquireAsync(key, new CancellationToken(true)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => a.AcquireAsync(key, TimeSpan.FromSeconds(5), new CancellationToken(true)));

        await using var handle = await Hold(b, key);
    }

    [TestMethod]
    public async Task Cancelling_mid_request_never_leaves_a_key_held_by_nobody_for_longer_than_a_lease()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var random = new Random(48);

        for (var round = 0; round < 5; round++)
        {
            var key = Key($"round-{round}");
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(random.Next(0, 25)));

            try
            {
                await using var handle = (await a.TryAcquireAsync(key, cts.Token)).Value;
            }
            catch (OperationCanceledException)
            {
                // A request cancelled in flight may still have been granted; the lease must then expire on its own,
                // and a connection-held lock is freed with its connection.
            }

            var bound = Provider.Expires ? Provider.Period + TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(5);
            var result = await b.AcquireAsync(key, bound, Ct);
            Assert.IsTrue(result.IsSuccess, $"Round {round}: {result}");
            await result.Value.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Cancelling_the_acquire_token_after_acquiring_does_not_release_the_lock()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("k");
        using var cts = new CancellationTokenSource();

        await using var handle = (await a.TryAcquireAsync(key, cts.Token)).Value;
        await cts.CancelAsync();
        await Task.Delay(Provider.Period / 2, Ct);

        Assert.IsFalse(handle.LostToken.IsCancellationRequested);
        Assert.AreEqual(LockingErrors.Busy, (await b.TryAcquireAsync(key, Ct)).Error?.Code);
    }

    [TestMethod]
    public async Task Renewal_keeps_the_lock_over_many_lease_periods()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("long-job");
        var handle = await Hold(a, key);
        var hold = Provider.Expires ? Provider.Period * 3 : Provider.Period * 8;
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < hold)
        {
            Assert.AreEqual(LockingErrors.Busy, (await b.TryAcquireAsync(key, Ct)).Error?.Code, $"Free after {watch.Elapsed}.");
            handle.ThrowIfLost();
            await Task.Delay(Provider.Period / 4, Ct);
        }

        Assert.IsFalse(handle.IsLost);
        await handle.DisposeAsync();
        await using var next = await Hold(b, key);
    }

    [TestMethod]
    public async Task Fencing_tokens_grow_with_every_holder()
    {
        var processes = new[] { Provider.NewProcess(), Provider.NewProcess(), Provider.NewProcess() };
        var key = Key("fenced");
        var tokens = new List<long>();

        for (var round = 0; round < 6; round++)
        {
            await using var handle = await Hold(processes[round % 3], key);
            Assert.IsNotNull(handle.FencingToken, "Every test backend issues fencing tokens.");
            tokens.Add(handle.FencingToken.Value);
        }

        for (var i = 1; i < tokens.Count; i++)
        {
            Assert.IsGreaterThan(tokens[i - 1], tokens[i], string.Join(", ", tokens));
        }
    }

    [TestMethod]
    public async Task Waiters_in_one_process_are_served_in_arrival_order()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = Key("queue");
        var held = await Hold(a, key);
        var order = new List<int>();
        var waiters = new List<Task>();

        for (var i = 0; i < 5; i++)
        {
            var id = i;
            waiters.Add(Task.Run(async () =>
            {
                var result = await b.AcquireAsync(key, TimeSpan.FromMinutes(1), Ct);
                Assert.IsTrue(result.IsSuccess, result.ToString());

                lock (order)
                {
                    order.Add(id);
                }

                await result.Value.DisposeAsync();
            }, Ct));

            await Task.Delay(100, Ct);
        }

        await held.DisposeAsync();
        await Task.WhenAll(waiters);

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, order);
    }

    private string Key(string name) => _prefix + name;

    private async Task<ILockHandle> Hold(IDistributedLock locks, string key)
    {
        var result = await locks.TryAcquireAsync(key, Ct);
        Assert.IsTrue(result.IsSuccess, $"{key}: {result}");
        return result.Value;
    }
}

[TestClass]
public sealed class InMemoryContractTests : LockContract
{
    private static readonly InMemoryUnderTest Shared = new();

    internal override ProviderUnderTest Provider => Shared;
}

[TestClass]
public sealed class AzureBlobLeaseContractTests : LockContract
{
    private static readonly AzureUnderTest Shared = new(() => TestHost.Azurite.ConnectionString);

    internal override ProviderUnderTest Provider => Shared;
}

[TestClass]
public sealed class PostgresAdvisoryLockContractTests : LockContract
{
    private static readonly PostgresUnderTest Shared = new(() => TestHost.Postgres.ConnectionString);

    internal override ProviderUnderTest Provider => Shared;
}
