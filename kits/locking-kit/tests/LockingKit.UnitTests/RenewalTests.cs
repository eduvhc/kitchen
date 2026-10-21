namespace LockingKit.UnitTests;

/// <summary>
/// The renewal scheduler on a fake clock. A 30 s lease renews every 10 s and gives up at 27 s after the start of the
/// last successful renewal; a connection-held lock is checked every 5 s and lost when a check fails or outlasts it.
/// </summary>
[TestClass]
public sealed class RenewalTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [TestMethod]
    public async Task A_held_lease_renews_every_third_of_its_duration_for_as_long_as_it_is_held()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, time);
        await using var handle = await locks.HoldAsync();

        for (var i = 1; i <= 12; i++)
        {
            await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            await Engines.UntilAsync(() => backend.Renewals == i, $"renewal {i}");
        }

        // Twelve renewals: four lease durations, never lost.
        Assert.IsFalse(handle.IsLost);
        Assert.IsFalse(handle.LostToken.IsCancellationRequested);
        handle.ThrowIfLost();
    }

    [TestMethod]
    public async Task Renewals_that_keep_failing_are_retried_then_the_lock_is_lost_exactly_at_the_deadline()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRenew = _ => throw new TimeoutException("backend down");
        await using var locks = Engines.Create(backend, time);
        await using var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(26.9), TimeSpan.FromMilliseconds(500));
        Assert.IsFalse(handle.IsLost, "Lost before the deadline.");
        Assert.IsGreaterThanOrEqualTo(6, backend.Renewals, "A failed renewal is retried every 2.5 s.");

        await time.AdvanceAsync(TimeSpan.FromMilliseconds(100));
        await Engines.UntilAsync(() => handle.IsLost, "the deadline passed");

        Assert.AreEqual(LockLossReason.Expired, handle.LossReason);
        Assert.IsTrue(handle.LostToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task A_renewal_that_never_returns_cannot_postpone_the_loss()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRenew = _ => new TaskCompletionSource<bool>().Task;   // ignores cancellation, never answers
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(26.5));
        Assert.IsFalse(handle.IsLost);
        Assert.AreEqual(1, backend.Renewals, "One renewal is stuck in flight.");

        await time.AdvanceAsync(TimeSpan.FromSeconds(0.5));
        await Engines.UntilAsync(() => handle.IsLost, "the deadline timer fired");
        Assert.AreEqual(LockLossReason.Expired, handle.LossReason);

        // Disposal does not wait forever for the stuck renewal either: it gives up after the release timeout.
        var disposing = handle.DisposeAsync().AsTask();
        await time.AdvanceAsync(TimeSpan.FromSeconds(6));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, backend.Releases);
    }

    [TestMethod]
    public async Task A_deadline_timer_that_fires_early_waits_out_the_rest_instead_of_giving_up_the_deadline()
    {
        var fake = Engines.Clock();
        var time = new EarlyTimers(fake);
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRenew = _ => new TaskCompletionSource<bool>().Task;
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        try
        {
            await fake.AdvanceAsync(TimeSpan.FromSeconds(26.99), TimeSpan.FromMilliseconds(10_000 - 10));
            Assert.IsFalse(handle.IsLost, "Lost before the deadline.");

            // The timer fires 5 ms early; the loss must still come, at the deadline. Only the timer can report it: the
            // renewal never returns.
            await fake.AdvanceAsync(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(5));
            await Engines.UntilAsync(() => handle.IsLost, "the deadline passed", timeoutMs: 1000);
            Assert.AreEqual(LockLossReason.Expired, handle.LossReason);
        }
        finally
        {
            // The stuck renewal holds disposal up to the release timeout, on the fake clock.
            var disposing = handle.DisposeAsync().AsTask();
            await fake.AdvanceAsync(TimeSpan.FromSeconds(6));
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task A_renewal_the_backend_refuses_loses_the_lock_at_once_as_revoked()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRenew = _ => Task.FromResult(false);
        await using var locks = Engines.Create(backend, time);
        await using var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await Engines.UntilAsync(() => handle.IsLost, "the renewal was refused");

        Assert.AreEqual(LockLossReason.Revoked, handle.LossReason);
        Assert.AreEqual(1, backend.Renewals, "No retries after a definite answer.");

        await time.AdvanceAsync(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, backend.Renewals, "A lost lock is never renewed again.");
    }

    [TestMethod]
    public async Task The_deadline_counts_from_when_a_renewal_was_sent_not_from_when_it_was_answered()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        var calls = 0;

        // The first renewal takes 5 s to succeed; every later one hangs.
        backend.OnRenew = ct => Interlocked.Increment(ref calls) == 1
            ? Task.Delay(TimeSpan.FromSeconds(5), time, ct).ContinueWith(_ => true, TaskScheduler.Default)
            : new TaskCompletionSource<bool>().Task;

        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        // Sent at 10 s, answered at 15 s: the deadline is 10 + 27 = 37 s, not 42 s.
        await time.AdvanceAsync(TimeSpan.FromSeconds(36.5), TimeSpan.FromMilliseconds(500));
        Assert.IsFalse(handle.IsLost);

        await time.AdvanceAsync(TimeSpan.FromSeconds(0.5));
        await Engines.UntilAsync(() => handle.IsLost, "37 s passed");
        Assert.AreEqual(LockLossReason.Expired, handle.LossReason);

        // The stuck renewal holds disposal up to the release timeout.
        var disposing = handle.DisposeAsync().AsTask();
        await time.AdvanceAsync(TimeSpan.FromSeconds(6));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task A_successful_renewal_after_failures_pushes_the_deadline_back()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        var failing = true;
        backend.OnRenew = _ => Volatile.Read(ref failing) ? throw new TimeoutException() : Task.FromResult(true);
        await using var locks = Engines.Create(backend, time);
        await using var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(500));
        Volatile.Write(ref failing, false);

        // Past the original 27 s deadline, and on to several more lease periods.
        await time.AdvanceAsync(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(1));

        Assert.IsFalse(handle.IsLost);
    }

    [TestMethod]
    public async Task A_connection_held_lock_is_checked_every_interval_and_a_failed_check_is_a_lost_connection()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.ConnectionHeld(TimeSpan.FromSeconds(5));
        await using var locks = Engines.Create(backend, time);
        await using var handle = await locks.HoldAsync();

        await time.AdvanceAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));
        Assert.AreEqual(6, backend.Renewals);
        Assert.IsFalse(handle.IsLost);

        backend.OnRenew = _ => throw new IOException("connection reset");
        await time.AdvanceAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        await Engines.UntilAsync(() => handle.IsLost, "the check failed");

        Assert.AreEqual(LockLossReason.ConnectionLost, handle.LossReason);
    }

    [TestMethod]
    public async Task A_connection_check_that_does_not_answer_within_the_interval_is_a_lost_connection()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.ConnectionHeld(TimeSpan.FromSeconds(5));
        backend.OnRenew = _ => new TaskCompletionSource<bool>().Task;
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();

        // The check goes out at 5 s and has until 10 s.
        await time.AdvanceAsync(TimeSpan.FromSeconds(9.5), TimeSpan.FromMilliseconds(500));
        Assert.IsFalse(handle.IsLost);

        await time.AdvanceAsync(TimeSpan.FromSeconds(0.5));
        await Engines.UntilAsync(() => handle.IsLost, "the check outlasted its interval");
        Assert.AreEqual(LockLossReason.ConnectionLost, handle.LossReason);

        var disposing = handle.DisposeAsync().AsTask();
        await time.AdvanceAsync(TimeSpan.FromSeconds(6));
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task A_loss_is_signalled_once_however_many_causes_race_to_report_it()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);

        // Revoked at the same moment the deadline would fire.
        backend.OnRenew = async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(17), time, CancellationToken.None);
            return false;
        };

        await using var locks = Engines.Create(backend, time);
        await using var handle = await locks.HoldAsync();
        var callbacks = 0;
        handle.LostToken.Register(() => Interlocked.Increment(ref callbacks));

        await time.AdvanceAsync(TimeSpan.FromSeconds(40), TimeSpan.FromMilliseconds(500));
        await Engines.UntilAsync(() => Volatile.Read(ref callbacks) > 0, "the loss callback ran");
        await Engines.SettleAsync();

        Assert.AreEqual(1, callbacks);
        Assert.IsNotNull(handle.LossReason);
    }

    [TestMethod]
    public async Task EnsureHeld_reports_a_loss_as_a_result_and_ThrowIfLost_as_a_cancellation()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRenew = _ => Task.FromResult(false);
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync("orders/42");

        handle.ThrowIfLost();
        Assert.IsTrue(handle.EnsureHeld().IsSuccess);

        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await Engines.UntilAsync(() => handle.IsLost, "the lock was revoked");

        var check = handle.EnsureHeld();
        Assert.AreEqual(LockingErrors.Lost, check.Error?.Code);
        Assert.AreEqual(ResultingKit.ErrorKind.Conflict, check.Error?.Kind);
        Assert.AreEqual("The lock 'orders/42' was lost (Revoked).", check.Error?.Message);

        var lost = Assert.ThrowsExactly<LockLostException>(handle.ThrowIfLost);
        Assert.IsInstanceOfType<OperationCanceledException>(lost);
        Assert.AreEqual("orders/42", lost.Key);
        Assert.AreEqual(LockLossReason.Revoked, lost.Reason);
        Assert.AreEqual(handle.LostToken, lost.CancellationToken);

        await handle.DisposeAsync();
        Assert.ThrowsExactly<ObjectDisposedException>(handle.ThrowIfLost);
        Assert.AreEqual(LockingErrors.Lost, handle.EnsureHeld().Error?.Code);
    }

    [TestMethod]
    public async Task A_throwing_LostToken_callback_does_not_break_the_handle()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnRenew = _ => Task.FromResult(false);
        await using var locks = Engines.Create(backend, time);
        var handle = await locks.HoldAsync();
        handle.LostToken.Register(() => throw new InvalidOperationException("user callback"));

        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await Engines.UntilAsync(() => handle.IsLost, "the lock was revoked");

        await handle.DisposeAsync();
        Assert.AreEqual(1, backend.Releases);
    }
}
