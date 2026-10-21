namespace LockingKit.UnitTests;

[TestClass]
public sealed class WaitingRoomTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task Turns_go_in_arrival_order_and_one_at_a_time()
    {
        var room = new WaitingRoom(TimeProvider.System);
        var first = await room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);
        var order = new List<int>();

        var waiters = Enumerable.Range(1, 5).Select(i => Task.Run(async () =>
        {
            await Task.Delay(i * 20, Ct);
            using var turn = await room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);

            lock (order)
            {
                order.Add(i);
            }
        }, Ct)).ToArray();

        await Task.Delay(200, Ct);
        Assert.IsEmpty(order, "Nobody gets a turn while it is taken.");

        first!.Dispose();
        await Task.WhenAll(waiters);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, order);
        Assert.AreEqual(0, room.LineCount);
    }

    [TestMethod]
    public async Task Keys_have_separate_lines()
    {
        var room = new WaitingRoom(TimeProvider.System);
        using var a = await room.WaitForTurnAsync("a", Timeout.InfiniteTimeSpan, Ct);
        using var b = await room.WaitForTurnAsync("b", TimeSpan.Zero, Ct);

        Assert.IsNotNull(b);
    }

    [TestMethod]
    public async Task A_waiter_that_times_out_leaves_the_line_and_the_next_one_still_gets_its_turn()
    {
        var time = Engines.Clock();
        var room = new WaitingRoom(time);
        var front = await room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);

        var impatient = room.WaitForTurnAsync("k", TimeSpan.FromSeconds(1), Ct);
        var patient = room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);

        await time.AdvanceAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(await impatient);

        front!.Dispose();
        using var turn = await patient.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.IsNotNull(turn);
    }

    [TestMethod]
    public async Task A_cancelled_waiter_throws_and_leaves_the_line()
    {
        var room = new WaitingRoom(TimeProvider.System);
        var front = await room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);
        using var cts = new CancellationTokenSource();

        var cancelled = room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, cts.Token);
        var next = room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);

        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);

        front!.Dispose();
        using var turn = await next.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.IsNotNull(turn);
    }

    [TestMethod]
    public async Task A_turn_handed_over_while_its_waiter_gives_up_is_passed_on_not_lost()
    {
        for (var round = 0; round < 200; round++)
        {
            var room = new WaitingRoom(TimeProvider.System);
            var front = await room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);
            using var cts = new CancellationTokenSource();

            var racing = room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, cts.Token);
            var last = room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);

            // Cancel and hand over on the same beat.
            var cancel = Task.Run(() => cts.Cancel(), Ct);
            front!.Dispose();
            await cancel;

            try
            {
                (await racing)?.Dispose();
            }
            catch (OperationCanceledException)
            {
            }

            using var turn = await last.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.IsNotNull(turn, $"Round {round}: the turn was lost.");
        }
    }

    [TestMethod]
    public async Task A_pulse_cuts_a_pause_short_even_when_it_lands_before_the_pause_starts()
    {
        var room = new WaitingRoom(TimeProvider.System);
        using var turn = await room.WaitForTurnAsync("k", Timeout.InfiniteTimeSpan, Ct);

        var signal = turn!.ReleaseSignal();
        room.Pulse("k");

        var paused = System.Diagnostics.Stopwatch.StartNew();
        await turn.PauseAsync(TimeSpan.FromMinutes(5), signal, Ct);

        Assert.IsLessThan(TimeSpan.FromSeconds(2), paused.Elapsed);
    }

    [TestMethod]
    public async Task Pulsing_a_key_nobody_waits_for_does_nothing()
    {
        var room = new WaitingRoom(TimeProvider.System);
        room.Pulse("nobody");
        Assert.AreEqual(0, room.LineCount);
        await Task.CompletedTask;
    }
}
