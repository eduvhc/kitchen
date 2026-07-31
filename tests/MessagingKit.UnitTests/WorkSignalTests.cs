namespace MessagingKit.UnitTests;

[TestClass]
public class WorkSignalTests
{
    private sealed class TestSignal : WorkSignal;

    [TestMethod]
    public async Task Wakes_immediately_once_pulsed()
    {
        using var signal = new TestSignal();
        signal.Pulse();

        Assert.IsTrue(await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [TestMethod]
    public async Task Times_out_when_nothing_pulses()
    {
        using var signal = new TestSignal();

        Assert.IsFalse(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [TestMethod]
    public async Task Coalesces_repeated_pulses_into_one_wake()
    {
        using var signal = new TestSignal();

        signal.Pulse();
        signal.Pulse();
        signal.Pulse();

        Assert.IsTrue(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.IsFalse(
            await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None),
            "several rows written together should wake the loop once; it drains what it finds");
    }

    [TestMethod]
    public async Task Wakes_a_waiter_that_was_already_waiting()
    {
        using var signal = new TestSignal();

        var waiting = signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        signal.Pulse();

        Assert.IsTrue(await waiting);
    }

    [TestMethod]
    public async Task Observes_cancellation()
    {
        using var signal = new TestSignal();
        using var cts = new CancellationTokenSource();

        var waiting = signal.WaitAsync(TimeSpan.FromSeconds(30), cts.Token);
        await cts.CancelAsync();

        // OperationCanceledException, not the TaskCanceledException subtype — the dispatcher loops
        // catch the base type, so shutdown is handled.
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => waiting);
    }

    [TestMethod]
    public void Pulsing_after_dispose_does_not_throw()
    {
        var signal = new TestSignal();
        signal.Dispose();

        // Shutdown races a commit; losing the wake is fine, crashing is not.
        signal.Pulse();
    }

    [TestMethod]
    public void Dispose_is_idempotent()
    {
        var signal = new TestSignal();

        signal.Dispose();
        signal.Dispose();
    }
}
