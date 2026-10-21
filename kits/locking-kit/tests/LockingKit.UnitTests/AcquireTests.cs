using ResultingKit;

namespace LockingKit.UnitTests;

[TestClass]
public sealed class AcquireTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task A_held_key_is_busy_a_conflict_result_and_not_an_exception()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        await using var locks = Engines.Create(backend, time);

        var result = await locks.TryAcquireAsync("orders/42", TestContext.CancellationToken);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(LockingErrors.Busy, result.Error.Code);
        Assert.AreEqual(ErrorKind.Conflict, result.Error.Kind);
        Assert.IsFalse(result.Error.IsTransient);
        StringAssert.Contains(result.Error.Message, "orders/42");
    }

    [TestMethod]
    public async Task Invalid_keys_are_validation_errors_and_never_reach_the_backend()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, Engines.Clock());

        // Built in code: attribute metadata cannot carry a lone surrogate.
        foreach (var key in new[] { string.Empty, "\ud800", "a\udc00b", "\udbff" })
        {
            var tried = await locks.TryAcquireAsync(key, TestContext.CancellationToken);
            var waited = await locks.AcquireAsync(key, TimeSpan.FromSeconds(1), TestContext.CancellationToken);

            Assert.AreEqual(LockingErrors.InvalidKey, tried.Error?.Code, $"key of length {key.Length}");
            Assert.AreEqual(ErrorKind.Validation, tried.Error?.Kind);
            Assert.AreEqual(LockingErrors.InvalidKey, waited.Error?.Code);
        }

        Assert.AreEqual(0, backend.Attempts);
    }

    [TestMethod]
    public async Task Keys_up_to_the_limit_are_accepted_and_longer_ones_are_not()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, Engines.Clock());

        await using var longest = (await locks.TryAcquireAsync(new string('k', DistributedLocks.MaxKeyLength), TestContext.CancellationToken)).Value;
        var tooLong = await locks.TryAcquireAsync(new string('k', DistributedLocks.MaxKeyLength + 1), TestContext.CancellationToken);

        Assert.AreEqual(LockingErrors.InvalidKey, tooLong.Error?.Code);
    }

    [TestMethod]
    public async Task A_null_key_is_a_validation_result_too()
    {
        await using var locks = Engines.Create(ScriptedBackend.Expiring(Lease), Engines.Clock());

        var result = await locks.TryAcquireAsync(null!, TestContext.CancellationToken);

        Assert.AreEqual(LockingErrors.InvalidKey, result.Error?.Code);
    }

    [TestMethod]
    public async Task A_negative_timeout_is_a_validation_error_and_infinite_is_allowed()
    {
        await using var locks = Engines.Create(ScriptedBackend.Expiring(Lease), Engines.Clock());

        var negative = await locks.AcquireAsync("k", TimeSpan.FromSeconds(-1), TestContext.CancellationToken);
        await using var infinite = (await locks.AcquireAsync("k", Timeout.InfiniteTimeSpan, TestContext.CancellationToken)).Value;

        Assert.AreEqual(LockingErrors.InvalidArgument, negative.Error?.Code);
        Assert.AreEqual(ErrorKind.Validation, negative.Error?.Kind);
    }

    [TestMethod]
    public async Task Timeouts_and_connection_failures_are_transient_unavailable_results()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, Engines.Clock());

        foreach (var failure in new Exception[]
        {
            new TimeoutException("slow"),
            new HttpRequestException("refused"),
            new System.Net.Sockets.SocketException(10061),
            new IOException("reset", new System.Net.Sockets.SocketException(10054)),
            new AggregateException(new TimeoutException("retry 1"), new HttpRequestException("retry 2")),
            new OperationCanceledException("an SDK timeout, not the caller"),
        })
        {
            backend.OnAcquire = (_, _) => throw failure;

            var result = await locks.TryAcquireAsync("k", TestContext.CancellationToken);

            Assert.AreEqual(LockingErrors.Unavailable, result.Error?.Code, failure.GetType().Name);
            Assert.IsTrue(result.Error!.IsTransient);
        }
    }

    [TestMethod]
    public async Task The_backend_classifies_its_own_exceptions_and_anything_unclassified_is_a_failure_result()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnTranslate = ex => ex is ArithmeticException ? LockingErrors.BackendUnavailable("throttled") : null;
        await using var locks = Engines.Create(backend, Engines.Clock());

        backend.OnAcquire = (_, _) => throw new ArithmeticException();
        Assert.AreEqual(LockingErrors.Unavailable, (await locks.TryAcquireAsync("k", TestContext.CancellationToken)).Error?.Code);

        backend.OnTranslate = ex => ex switch
        {
            ArithmeticException => LockingErrors.BackendUnavailable("throttled"),
            UnauthorizedAccessException => LockingErrors.Misconfiguration("wrong credentials"),
            _ => null,
        };
        backend.OnAcquire = (_, _) => throw new UnauthorizedAccessException("wrong credentials");
        var misconfigured = await locks.TryAcquireAsync("k", TestContext.CancellationToken);
        Assert.AreEqual(LockingErrors.Misconfigured, misconfigured.Error?.Code);
        Assert.AreEqual(ErrorKind.Failure, misconfigured.Error?.Kind);

        backend.OnAcquire = (_, _) => throw new InvalidOperationException("something nobody expected");
        var failed = await locks.TryAcquireAsync("k", TestContext.CancellationToken);
        Assert.AreEqual(LockingErrors.BackendFailed, failed.Error?.Code);
        Assert.AreEqual(ErrorKind.Failure, failed.Error?.Kind);
        StringAssert.Contains(failed.Error!.Message, "System.InvalidOperationException: something nobody expected");
    }

    [TestMethod]
    public async Task Waiting_stops_at_the_first_failure_no_retry_can_fix()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => throw new InvalidOperationException("broken");
        await using var locks = Engines.Create(backend, Engines.Clock());

        var result = await locks.AcquireAsync("k", TimeSpan.FromHours(1), TestContext.CancellationToken);

        Assert.AreEqual(LockingErrors.BackendFailed, result.Error?.Code);
        Assert.AreEqual(1, backend.Attempts);
    }

    [TestMethod]
    public async Task A_cancelled_token_throws_before_any_attempt()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, Engines.Clock());

        await Assert.ThrowsAsync<OperationCanceledException>(() => locks.TryAcquireAsync("k", new CancellationToken(true)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => locks.AcquireAsync("k", TimeSpan.FromMinutes(1), new CancellationToken(true)));
        Assert.AreEqual(0, backend.Attempts);
    }

    [TestMethod]
    public async Task Cancelling_during_an_attempt_throws_the_cancellation()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        };
        await using var locks = Engines.Create(backend, Engines.Clock());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() => locks.TryAcquireAsync("k", cts.Token));
    }

    [TestMethod]
    public async Task Cancelling_during_the_backoff_pause_throws_the_cancellation()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        var options = new TestLockingOptions { MinRetryDelay = TimeSpan.FromMinutes(5), MaxRetryDelay = TimeSpan.FromMinutes(5) };
        await using var locks = Engines.Create(backend, Engines.Clock(), options);
        using var cts = new CancellationTokenSource();

        var waiting = locks.AcquireAsync("k", TimeSpan.FromHours(1), cts.Token);
        await Engines.UntilAsync(() => backend.Attempts == 1, "the first attempt");
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, backend.Attempts);
    }

    [TestMethod]
    public async Task Cancelling_while_queued_behind_a_local_waiter_throws_and_leaves_the_queue()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        await using var locks = Engines.Create(backend, Engines.Clock());
        using var cts = new CancellationTokenSource();

        var front = locks.AcquireAsync("k", TimeSpan.FromHours(1), TestContext.CancellationToken);
        await Engines.UntilAsync(() => backend.Attempts == 1, "the front waiter's first attempt");
        var queued = locks.AcquireAsync("k", TimeSpan.FromHours(1), cts.Token);
        await Engines.SettleAsync();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(front.IsCompleted);
    }

    [TestMethod]
    public async Task A_lease_granted_as_the_caller_cancels_is_released_at_once_not_leaked()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        using var cts = new CancellationTokenSource();
        ScriptedBackend.ScriptedLease? granted = null;
        backend.OnAcquire = (_, _) =>
        {
            granted = backend.Grant();
            cts.Cancel();   // the caller gives up while the grant is on its way back
            return Task.FromResult<ILockLease?>(granted);
        };
        await using var locks = Engines.Create(backend, Engines.Clock());

        await Assert.ThrowsAsync<OperationCanceledException>(() => locks.TryAcquireAsync("k", cts.Token));

        Assert.AreEqual(1, backend.Releases);
        Assert.IsTrue(granted!.Disposed);
        Assert.AreEqual(0, locks.LiveHandles);
    }

    [TestMethod]
    public async Task Cancelling_the_acquire_token_after_acquiring_does_not_touch_the_handle()
    {
        var backend = ScriptedBackend.Expiring(Lease);
        await using var locks = Engines.Create(backend, Engines.Clock());
        using var cts = new CancellationTokenSource();

        await using var handle = (await locks.TryAcquireAsync("k", cts.Token)).Value;
        await cts.CancelAsync();

        Assert.IsFalse(handle.LostToken.IsCancellationRequested);
        Assert.AreEqual(0, backend.Releases);
    }

    [TestMethod]
    public async Task Waiting_retries_with_doubling_capped_backoff_until_the_lock_frees()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        var attemptsAt = new List<TimeSpan>();
        var start = time.GetTimestamp();
        backend.OnAcquire = (_, _) =>
        {
            lock (attemptsAt)
            {
                attemptsAt.Add(time.GetElapsedTime(start));
                return Task.FromResult<ILockLease?>(attemptsAt.Count < 7 ? null : backend.Grant());
            }
        };

        var options = new TestLockingOptions { MinRetryDelay = TimeSpan.FromMilliseconds(100), MaxRetryDelay = TimeSpan.FromMilliseconds(800) };
        await using var locks = Engines.Create(backend, time, options, random: () => 1.0);

        var acquiring = locks.AcquireAsync("k", TimeSpan.FromMinutes(1), TestContext.CancellationToken);
        await time.AdvanceAsync(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(50));
        await using var handle = (await acquiring).Value;

        // With the jitter at its top, pauses are 100, 200, 400, 800, 800, 800 ms: capped, never growing past the cap.
        var pauses = attemptsAt.Zip(attemptsAt.Skip(1), (a, b) => (b - a).TotalMilliseconds).ToArray();
        CollectionAssert.AreEqual(new double[] { 100, 200, 400, 800, 800, 800 }, pauses);
    }

    [TestMethod]
    public async Task Waiting_gives_up_with_a_timeout_result_after_a_last_attempt_at_the_deadline()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        await using var locks = Engines.Create(backend, time);

        var acquiring = locks.AcquireAsync("k", TimeSpan.FromSeconds(3), TestContext.CancellationToken);
        await time.AdvanceAsync(TimeSpan.FromSeconds(2.9), TimeSpan.FromMilliseconds(50));
        Assert.IsFalse(acquiring.IsCompleted);

        await time.AdvanceAsync(TimeSpan.FromSeconds(0.2), TimeSpan.FromMilliseconds(50));
        var result = await acquiring.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(LockingErrors.Timeout, result.Error?.Code);
        Assert.AreEqual(ErrorKind.Conflict, result.Error?.Kind);
        Assert.IsGreaterThan(3, backend.Attempts);
    }

    [TestMethod]
    public async Task Waiting_rides_out_backend_failures_and_reports_the_last_one_when_it_never_recovers()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => throw new TimeoutException("down");
        await using var locks = Engines.Create(backend, time);

        var acquiring = locks.AcquireAsync("k", TimeSpan.FromSeconds(2), TestContext.CancellationToken);
        await time.AdvanceAsync(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
        var result = await acquiring.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(LockingErrors.Unavailable, result.Error?.Code);
        Assert.IsGreaterThan(2, backend.Attempts);

        // And a failure that clears is ridden out.
        var calls = 0;
        backend.OnAcquire = (_, _) => Interlocked.Increment(ref calls) < 3 ? throw new TimeoutException() : Task.FromResult<ILockLease?>(backend.Grant());
        acquiring = locks.AcquireAsync("k", TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        await time.AdvanceAsync(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));
        await using var handle = (await acquiring.WaitAsync(TimeSpan.FromSeconds(5))).Value;
    }

    [TestMethod]
    public async Task Waiting_without_a_timeout_stops_only_when_cancelled()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        await using var locks = Engines.Create(backend, time);
        using var cts = new CancellationTokenSource();

        var acquiring = locks.AcquireAsync("k", cts.Token);
        await time.AdvanceAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(1));
        Assert.IsFalse(acquiring.IsCompleted);

        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => acquiring.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task A_local_release_wakes_the_waiter_at_once_without_waiting_out_its_pause()
    {
        var time = Engines.Clock();
        var backend = new InMemoryLockBackend(Lease, time);
        await using var locks = Engines.Create(backend, time, new TestLockingOptions { MinRetryDelay = TimeSpan.FromMinutes(1), MaxRetryDelay = TimeSpan.FromMinutes(1) });
        var holder = await locks.HoldAsync("k");

        var waiting = locks.AcquireAsync("k", TimeSpan.FromHours(1), TestContext.CancellationToken);
        await Engines.SettleAsync();
        Assert.IsFalse(waiting.IsCompleted);

        // The clock does not move: only the release can wake the waiter from its one-minute pause.
        await holder.DisposeAsync();
        await using var handle = (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Value;
    }

    [TestMethod]
    public async Task Local_waiters_are_served_in_arrival_order()
    {
        var time = Engines.Clock();
        var backend = new InMemoryLockBackend(Lease, time);
        await using var locks = Engines.Create(backend, time, new TestLockingOptions { MinRetryDelay = TimeSpan.FromMinutes(1), MaxRetryDelay = TimeSpan.FromMinutes(1) });
        var holder = await locks.HoldAsync("k");
        var order = new List<int>();

        var waiters = new List<Task>();

        for (var i = 0; i < 8; i++)
        {
            var id = i;
            waiters.Add(Task.Run(async () =>
            {
                await using var handle = (await locks.AcquireAsync("k", TimeSpan.FromHours(1), TestContext.CancellationToken)).Value;

                lock (order)
                {
                    order.Add(id);
                }
            }));

            await Engines.SettleAsync();
        }

        await holder.DisposeAsync();
        await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(10));

        CollectionAssert.AreEqual(Enumerable.Range(0, 8).ToList(), order);
        Assert.AreEqual(0, locks.Room.LineCount, "Lines are removed once nobody waits.");
    }

    [TestMethod]
    public async Task Only_the_front_waiter_polls_the_backend()
    {
        var time = Engines.Clock();
        var backend = ScriptedBackend.Expiring(Lease);
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        await using var locks = Engines.Create(backend, time, new TestLockingOptions { MinRetryDelay = TimeSpan.FromSeconds(1), MaxRetryDelay = TimeSpan.FromSeconds(1) });

        var waiters = Enumerable.Range(0, 20)
            .Select(_ => locks.AcquireAsync("k", TimeSpan.FromSeconds(10), TestContext.CancellationToken))
            .ToArray();

        await time.AdvanceAsync(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250));

        // Twenty callers, one stream of about one attempt a second.
        Assert.IsLessThanOrEqualTo(8, backend.Attempts, $"{backend.Attempts} attempts");

        await time.AdvanceAsync(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(250));
        var results = await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(results.All(r => r.Error?.Code == LockingErrors.Timeout));
    }
}
