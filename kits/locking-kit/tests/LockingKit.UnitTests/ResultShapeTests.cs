using ResultingKit;

namespace LockingKit.UnitTests;

[TestClass]
public sealed class ResultShapeTests
{
    [TestMethod]
    public void Contention_is_a_conflict_and_never_transient()
    {
        var busy = LockingErrors.Held("orders/42");
        var timeout = LockingErrors.WaitTimedOut("orders/42", TimeSpan.FromSeconds(2.5));

        Assert.AreEqual(new Error(LockingErrors.Busy, "The lock 'orders/42' is held by someone else.", ErrorKind.Conflict), busy);
        Assert.AreEqual(ErrorKind.Conflict, timeout.Kind);
        Assert.AreEqual(LockingErrors.Timeout, timeout.Code);
        StringAssert.Contains(timeout.Message, "2.5 s");
        Assert.IsFalse(busy.IsTransient);
        Assert.IsFalse(timeout.IsTransient);
    }

    [TestMethod]
    public void An_unreachable_backend_is_the_one_transient_outcome()
    {
        var error = LockingErrors.BackendUnavailable("503 ServerBusy");

        Assert.AreEqual(LockingErrors.Unavailable, error.Code);
        Assert.IsTrue(error.IsTransient);
        StringAssert.Contains(error.Message, "503 ServerBusy");
    }

    [TestMethod]
    public void Codes_are_stable_and_namespaced()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "locking.busy", "locking.timeout", "locking.invalid_key", "locking.invalid_argument", "locking.unavailable",
                "locking.misconfigured", "locking.backend_failed", "locking.disposed", "locking.lost",
            },
            new[]
            {
                LockingErrors.Busy, LockingErrors.Timeout, LockingErrors.InvalidKey, LockingErrors.InvalidArgument, LockingErrors.Unavailable,
                LockingErrors.Misconfigured, LockingErrors.BackendFailed, LockingErrors.Disposed, LockingErrors.Lost,
            });
    }

    [TestMethod]
    public void Failures_no_retry_fixes_are_plain_failures_and_a_released_lock_reads_as_released()
    {
        Assert.AreEqual(ErrorKind.Failure, LockingErrors.Misconfiguration("403 AuthorizationFailure").Kind);
        Assert.AreEqual(ErrorKind.Failure, LockingErrors.Unexpected(new InvalidOperationException("x")).Kind);
        Assert.AreEqual("The lock 'k' was released.", LockingErrors.NoLongerHeld("k", reason: null).Message);
    }

    [TestMethod]
    public void Results_read_naturally_at_the_call_site()
    {
        Result<ILockHandle> busy = LockingErrors.Held("k");

        var answer = busy.Match(_ => "acquired", e => e.Code == LockingErrors.Busy ? "try later" : "fail");

        Assert.AreEqual("try later", answer);
        Assert.AreEqual("Failure(locking.busy: The lock 'k' is held by someone else.)", busy.ToString());
    }

    [TestMethod]
    public void A_lost_lock_exception_names_the_key_and_reason()
    {
        using var cts = new CancellationTokenSource();
        var lost = new LockLostException("orders/42", LockLossReason.ConnectionLost, cts.Token);

        Assert.AreEqual("The lock 'orders/42' was lost (ConnectionLost).", lost.Message);
        Assert.AreEqual(cts.Token, lost.CancellationToken);
    }

    [TestMethod]
    public void Lease_timings_renew_at_a_third_and_give_up_a_tenth_before_expiry()
    {
        var expiring = LeaseTiming.Expiring(TimeSpan.FromSeconds(30));
        var connection = LeaseTiming.ConnectionHeld(TimeSpan.FromSeconds(5));

        Assert.AreEqual(TimeSpan.FromSeconds(10), expiring.RenewalInterval);
        Assert.AreEqual(TimeSpan.FromSeconds(27), expiring.GiveUpAfter);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5), expiring.RetryInterval);
        Assert.IsNull(connection.Duration);
        Assert.IsNull(connection.GiveUpAfter);
        Assert.AreEqual(TimeSpan.FromSeconds(5), connection.RenewalInterval);
    }
}
