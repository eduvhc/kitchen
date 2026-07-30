using MessagingKit.Inbox;
using MessagingKit.Inbox.Abstractions;
using MessagingKit.Inbox.Domain;
using MessagingKit.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.Tests.Inbox;

[TestClass]
public class InboxTests : MessagingTest
{
    [TestMethod]
    public async Task Stores_a_message_it_has_not_seen()
    {
        var stored = await StoreAsync(Envelope());

        Assert.IsTrue(stored);
        Assert.AreEqual(1, await WithDbAsync(db => db.Set<InboxMessage>().CountAsync()));
    }

    [TestMethod]
    public async Task Ignores_a_duplicate_message_id()
    {
        var envelope = Envelope();

        var first = await StoreAsync(envelope);
        var second = await StoreAsync(envelope);

        Assert.IsTrue(first);
        Assert.IsFalse(second);
        Assert.AreEqual(1, await WithDbAsync(db => db.Set<InboxMessage>().CountAsync()));
    }

    [TestMethod]
    public async Task Processes_a_stored_message_once()
    {
        await StoreAsync(Envelope());

        var processed = await RunAsync();

        Assert.AreEqual(1, processed);
        Assert.HasCount(1, TestHost.Handlers.Handled);
        Assert.AreEqual("ada@example.com", TestHost.Handlers.Handled.Single().Message.To);

        var stored = await StoredAsync();
        Assert.AreEqual(InboxStatus.Processed, stored.Status);
        Assert.AreEqual(TestHost.Clock.GetUtcNow(), stored.ProcessedAt);

        Assert.AreEqual(0, await RunAsync());
    }

    [TestMethod]
    public async Task Passes_context_to_the_handler()
    {
        var envelope = Envelope();
        await StoreAsync(envelope);

        await RunAsync();

        var context = TestHost.Handlers.Handled.Single().Context;
        Assert.AreEqual(envelope.Id, context.MessageId);
        Assert.AreEqual("send-email", context.Type);
        Assert.AreEqual(1, context.AttemptCount);
    }

    [TestMethod]
    public async Task Retries_a_failing_handler_with_backoff()
    {
        TestHost.Handlers.Behaviour = _ => throw new InvalidOperationException("downstream down");
        await StoreAsync(Envelope());

        await RunAsync();

        var stored = await StoredAsync();
        Assert.AreEqual(InboxStatus.Pending, stored.Status);
        Assert.Contains("downstream down", stored.LastError!);
        Assert.IsGreaterThan(TestHost.Clock.GetUtcNow(), stored.ScheduledAt);
    }

    [TestMethod]
    public async Task Marks_a_message_dead_once_attempts_are_exhausted()
    {
        TestHost.Handlers.Behaviour = _ => throw new InvalidOperationException("always fails");
        await StoreAsync(Envelope());

        await WithDbAsync(async db =>
            await db.Set<InboxMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxAttempts, 1)));

        await RunAsync();

        Assert.AreEqual(InboxStatus.Dead, (await StoredAsync()).Status);
    }

    [TestMethod]
    public async Task Marks_an_unknown_type_dead_immediately()
    {
        await StoreAsync(Envelope() with { Type = "not-registered" });

        await RunAsync();

        var stored = await StoredAsync();
        Assert.AreEqual(InboxStatus.Dead, stored.Status);
        Assert.Contains("not-registered", stored.LastError!);
        Assert.IsEmpty(TestHost.Handlers.Handled);
    }

    private static MessageEnvelope Envelope() => new()
    {
        Id = Guid.CreateVersion7(),
        Type = "send-email",
        Payload = """{"to":"ada@example.com","subject":"Hello"}""",
        CreatedAt = TestHost.Clock.GetUtcNow(),
    };

    private static Task<bool> StoreAsync(MessageEnvelope envelope) =>
        WithScopeAsync(services => services.GetRequiredService<IInbox>().TryStoreAsync(envelope));

    private static Task<int> RunAsync() =>
        TestHost.Services.GetRequiredService<InboxProcessor>().RunOnceAsync(CancellationToken.None);

    private static Task<InboxMessage> StoredAsync() =>
        WithDbAsync(db => db.Set<InboxMessage>().AsNoTracking().FirstAsync());
}
