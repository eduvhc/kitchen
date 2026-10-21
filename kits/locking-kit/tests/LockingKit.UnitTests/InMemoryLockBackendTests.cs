namespace LockingKit.UnitTests;

[TestClass]
public sealed class InMemoryLockBackendTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task A_lease_excludes_others_until_it_expires_and_then_anyone_can_take_it()
    {
        var time = Engines.Clock();
        var backend = new InMemoryLockBackend(Lease, time);

        var first = await backend.TryAcquireAsync("k", Ct);
        Assert.IsNotNull(first);
        Assert.IsNull(await backend.TryAcquireAsync("k", Ct));

        time.Advance(Lease - TimeSpan.FromMilliseconds(1));
        Assert.IsNull(await backend.TryAcquireAsync("k", Ct));

        time.Advance(TimeSpan.FromMilliseconds(1));
        var second = await backend.TryAcquireAsync("k", Ct);
        Assert.IsNotNull(second);

        Assert.IsFalse(await first.RenewAsync(Ct), "The old holder cannot renew a lease someone else took.");
        Assert.IsTrue(await second.RenewAsync(Ct));
    }

    [TestMethod]
    public async Task Like_Azure_an_expired_lease_can_be_renewed_while_nobody_else_has_taken_it()
    {
        var time = Engines.Clock();
        var backend = new InMemoryLockBackend(Lease, time);
        var lease = (await backend.TryAcquireAsync("k", Ct))!;

        time.Advance(Lease * 2);

        Assert.IsTrue(await lease.RenewAsync(Ct));
        Assert.IsNull(await backend.TryAcquireAsync("k", Ct));
    }

    [TestMethod]
    public async Task Releasing_frees_the_key_and_only_the_owner_can_release_it()
    {
        var time = Engines.Clock();
        var backend = new InMemoryLockBackend(Lease, time);
        var stale = (await backend.TryAcquireAsync("k", Ct))!;
        time.Advance(Lease);
        var current = (await backend.TryAcquireAsync("k", Ct))!;

        await stale.ReleaseAsync(Ct);
        Assert.AreEqual(1, backend.HeldCount, "A former holder's release does not free its successor's lease.");

        await current.ReleaseAsync(Ct);
        Assert.AreEqual(0, backend.HeldCount);
        Assert.IsNotNull(await backend.TryAcquireAsync("k", Ct));
    }

    [TestMethod]
    public async Task Every_acquisition_gets_a_larger_fencing_token()
    {
        var time = Engines.Clock();
        var backend = new InMemoryLockBackend(Lease, time);
        var tokens = new List<long>();

        for (var i = 0; i < 5; i++)
        {
            var lease = (await backend.TryAcquireAsync("k", Ct))!;
            tokens.Add(lease.FencingToken!.Value);
            await lease.ReleaseAsync(Ct);
            _ = await backend.TryAcquireAsync($"other-{i}", Ct);
        }

        CollectionAssert.AreEqual(tokens.Order().ToList(), tokens);
        Assert.AreEqual(tokens.Count, tokens.Distinct().Count());
    }

    [TestMethod]
    public void Leases_shorter_than_30_ms_are_refused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new InMemoryLockBackend(TimeSpan.FromMilliseconds(10), TimeProvider.System));
    }
}
