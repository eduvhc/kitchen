using MessagingKit.Outbox;
using MessagingKit.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.Tests.Outbox;

[TestClass]
public class OutboxSignalTests : MessagingTest
{
    [TestMethod]
    public async Task Wakes_the_dispatcher_when_a_commit_stages_rows()
    {
        var signal = Signal();
        await Drain(signal);

        await OutboxTests.AddAsync(new SendEmail("ada@example.com", "Hello"));

        Assert.IsTrue(
            await signal.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None),
            "committing outbox rows should wake the dispatcher instead of waiting out the poll interval");
    }

    [TestMethod]
    public async Task Stays_quiet_when_a_commit_stages_nothing()
    {
        var signal = Signal();
        await Drain(signal);

        await WithDbAsync(db => db.SaveChangesAsync());

        Assert.IsFalse(
            await signal.WaitAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None),
            "a save with no outbox rows should not wake the dispatcher");
    }

    private static OutboxSignal Signal() => TestHost.Services.GetRequiredService<OutboxSignal>();

    // Clears a pulse left by an earlier test so each case starts from a known state.
    private static Task<bool> Drain(OutboxSignal signal) => signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);
}
