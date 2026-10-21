namespace LockingKit.UnitTests;

[TestClass]
public sealed class DisposalTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [TestMethod]
    public async Task Disposing_releases_once_and_signals_the_end_of_protection_without_calling_it_a_loss()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        Assert.AreEqual(1, backend.Releases);
        Assert.IsTrue(handle.LostToken.IsCancellationRequested, "Work linked to the handle stops once it is released.");
        Assert.IsFalse(handle.IsLost);
        Assert.IsNull(handle.LossReason);
        Assert.AreEqual(0, locks.LiveHandles);
    }

    [TestMethod]
    public async Task Concurrent_disposes_release_once_and_all_wait_for_the_release()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        var release = new TaskCompletionSource();
        backend.OnRelease = _ => release.Task;
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        var disposes = Enumerable.Range(0, 10).Select(_ => handle.DisposeAsync().AsTask()).ToArray();
        await Engines.UntilAsync(() => backend.Releases == 1, "the release went out");
        await Engines.SettleAsync();

        Assert.IsTrue(disposes.All(d => !d.IsCompleted), "Every dispose waits for the one release.");

        release.SetResult();
        await Task.WhenAll(disposes).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, backend.Releases);
    }

    [TestMethod]
    public async Task Disposing_stops_renewal_for_good()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await Engines.UntilAsync(() => backend.Renewals == 1, "the first renewal");
        await handle.DisposeAsync();

        await time.AdvanceAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, backend.Renewals);
    }

    [TestMethod]
    public async Task Disposal_racing_an_in_flight_renewal_cancels_it_before_releasing()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        var renewing = new TaskCompletionSource();
        backend.OnRenew = async ct =>
        {
            renewing.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return true;
        };
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await renewing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handle.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        CollectionAssert.AreEqual(
            new[] { "acquire:jobs/nightly", "renew:start", "renew:cancelled", "release" },
            backend.Log.ToArray(),
            "The renewal is cancelled and finished before the release goes out, so it can never extend a released lease.");
    }

    [TestMethod]
    public async Task Disposing_never_surfaces_what_a_cancelled_renewal_threw()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        var renewing = new TaskCompletionSource();

        // Like the Azure SDK, which reports a cancelled retry loop as an AggregateException.
        backend.OnRenew = async ct =>
        {
            renewing.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException inner)
            {
                throw new AggregateException("Retry failed after 3 tries.", inner);
            }

            return true;
        };
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await renewing.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await handle.DisposeAsync();

        Assert.AreEqual(1, backend.Releases);
        Assert.IsFalse(handle.IsLost);
    }

    [TestMethod]
    public async Task Disposal_racing_renewal_many_times_never_renews_after_releasing()
    {
        var time = Engines.Clock();

        for (var round = 0; round < 50; round++)
        {
            var backend = ScriptedBackend.Expiring(Lease);
            backend.OnRenew = async ct =>
            {
                await Task.Yield();
                return true;
            };
            await using var locks = Engines.Create(backend, time);
            var handle = await locks.HoldAsync();

            // Fire the renewal timer and dispose on the same beat.
            time.Advance(TimeSpan.FromSeconds(10));
            await handle.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await Engines.SettleAsync();

            var log = backend.Log.ToArray();
            var release = Array.IndexOf(log, "release");
            Assert.AreEqual(log.Length - 1, release, $"Round {round}: {string.Join(", ", log)}");
        }
    }

    [TestMethod]
    public async Task A_release_that_hangs_is_abandoned_after_the_release_timeout()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRelease = _ => new TaskCompletionSource().Task;   // ignores cancellation
        await using var locks = Engines.Create(backend, time, new TestLockingOptions { ReleaseTimeout = TimeSpan.FromSeconds(2) });
        var handle = await locks.HoldAsync();

        var disposing = handle.DisposeAsync().AsTask();
        await time.AdvanceAsync(TimeSpan.FromSeconds(1.5), TimeSpan.FromMilliseconds(500));
        Assert.IsFalse(disposing.IsCompleted);

        await time.AdvanceAsync(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task A_release_that_fails_is_swallowed_because_expiry_frees_the_lock_anyway()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRelease = _ => throw new IOException("connection reset");
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        await handle.DisposeAsync();

        Assert.AreEqual(1, backend.Releases);
        Assert.AreEqual(0, locks.LiveHandles);
    }

    [TestMethod]
    public async Task A_lost_handle_still_releases_on_dispose_in_case_the_backend_still_has_it()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRenew = _ => throw new TimeoutException();
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(28), TimeSpan.FromSeconds(1));
        await Engines.UntilAsync(() => handle.IsLost, "the lease expired locally");
        await handle.DisposeAsync();

        Assert.AreEqual(1, backend.Releases);
        Assert.AreEqual(LockLossReason.Expired, handle.LossReason, "Disposing keeps the reason.");
    }

    [TestMethod]
    public async Task Disposing_the_provider_releases_every_handle_it_still_holds_and_disposes_the_backend()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        var locks = Engines.Create(backend, time);
        var first = await locks.HoldAsync("a");
        var second = await locks.HoldAsync("b");

        await locks.DisposeAsync();

        Assert.AreEqual(2, backend.Releases);
        Assert.IsTrue(backend.Disposed);
        Assert.IsTrue(first.LostToken.IsCancellationRequested);
        Assert.IsTrue(second.LostToken.IsCancellationRequested);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => locks.TryAcquireAsync("c"));
    }
}
