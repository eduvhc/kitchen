using MessagingKit.Inbox;
using MessagingKit.Inbox.Abstractions;
using MessagingKit.Inbox.Domain;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.Outbox;
using MessagingKit.Outbox.Domain;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResultingKit;

namespace MessagingKit.IntegrationTests;

[TestClass]
public class MetricsTests : MessagingTest
{
    private const string Type = "send-email";

    private const string SentMessages = "messaging.client.sent.messages";
    private const string SendDuration = "messaging.client.operation.duration";
    private const string ConsumedMessages = "messaging.client.consumed.messages";
    private const string ProcessDuration = "messaging.process.duration";
    private const string ReceivedMessages = "messagingkit.inbox.received.messages";

    [TestMethod]
    public async Task Counts_a_successful_send_and_times_it()
    {
        using var metrics = new MetricRecorder();
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Hello"));

        await DispatchAsync();

        var sent = metrics.For(SentMessages, Type).Single();
        Assert.AreEqual(1, sent.Value);
        Assert.AreEqual("{message}", sent.Unit);
        Assert.AreEqual("messagingkit", sent.Tag("messaging.system"));
        Assert.AreEqual("send", sent.Tag("messaging.operation.name"));
        Assert.AreEqual("send", sent.Tag("messaging.operation.type"));
        Assert.AreEqual("sent", sent.Tag("messagingkit.outcome"));
        Assert.IsFalse(sent.Tags.ContainsKey("error.type"), "a success must not carry error.type");

        var duration = metrics.For(SendDuration, Type).Single();
        Assert.AreEqual("s", duration.Unit);
        Assert.IsGreaterThan(0d, duration.Value);
        Assert.AreEqual("sent", duration.Tag("messagingkit.outcome"));
    }

    [TestMethod]
    public async Task Reports_a_transient_send_failure_as_retried()
    {
        using var metrics = new MetricRecorder();
        TestHost.Transport.Behaviour = _ => Error.Transient("broker.unreachable", "broker unreachable");
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Retry me"));

        await DispatchAsync();

        var sent = metrics.For(SentMessages, Type).Single();
        Assert.AreEqual("retried", sent.Tag("messagingkit.outcome"));
        Assert.AreEqual("broker.unreachable", sent.Tag("error.type"));
        Assert.AreEqual("retried", metrics.For(SendDuration, Type).Single().Tag("messagingkit.outcome"));
    }

    [TestMethod]
    public async Task Reports_a_permanent_send_failure_as_dead()
    {
        using var metrics = new MetricRecorder();
        TestHost.Transport.Behaviour = _ => Error.Failure("broker.unknown_destination", "unknown destination");
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Doomed"));

        await DispatchAsync();

        var sent = metrics.For(SentMessages, Type).Single();
        Assert.AreEqual("dead", sent.Tag("messagingkit.outcome"));
        Assert.AreEqual("broker.unknown_destination", sent.Tag("error.type"));
    }

    [TestMethod]
    public async Task Reports_the_last_send_attempt_as_dead()
    {
        using var metrics = new MetricRecorder();
        TestHost.Transport.Behaviour = _ => Error.Transient("broker.unreachable", "still down");
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Persistent"));
        await WithDbAsync(db => db.Set<OutboxMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxAttempts, 2)));

        await DispatchAsync();
        TestHost.Clock.Advance(TimeSpan.FromHours(1));
        await DispatchAsync();

        CollectionAssert.AreEqual(
            new[] { "retried", "dead" },
            metrics.For(SentMessages, Type).Select(m => m.Tag("messagingkit.outcome")).ToArray());
        Assert.HasCount(2, metrics.For(SendDuration, Type));
    }

    [TestMethod]
    public async Task Counts_a_processed_message_and_times_the_handler()
    {
        using var metrics = new MetricRecorder();
        await StoreAsync(Envelope());

        await ProcessAsync();

        var consumed = metrics.For(ConsumedMessages, Type).Single();
        Assert.AreEqual(1, consumed.Value);
        Assert.AreEqual("{message}", consumed.Unit);
        Assert.AreEqual("messagingkit", consumed.Tag("messaging.system"));
        Assert.AreEqual("process", consumed.Tag("messaging.operation.name"));
        Assert.AreEqual("processed", consumed.Tag("messagingkit.outcome"));
        Assert.IsFalse(consumed.Tags.ContainsKey("error.type"), "a success must not carry error.type");

        var duration = metrics.For(ProcessDuration, Type).Single();
        Assert.AreEqual("s", duration.Unit);
        Assert.IsGreaterThan(0d, duration.Value);
        Assert.AreEqual("processed", duration.Tag("messagingkit.outcome"));
    }

    [TestMethod]
    public async Task Reports_a_failing_handler_as_retried_then_dead()
    {
        using var metrics = new MetricRecorder();
        TestHost.Handlers.Behaviour = _ => throw new InvalidOperationException("downstream down");
        await StoreAsync(Envelope());
        await WithDbAsync(db => db.Set<InboxMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxAttempts, 2)));

        await ProcessAsync();
        TestHost.Clock.Advance(TimeSpan.FromHours(1));
        await ProcessAsync();

        var consumed = metrics.For(ConsumedMessages, Type);
        CollectionAssert.AreEqual(
            new[] { "retried", "dead" },
            consumed.Select(m => m.Tag("messagingkit.outcome")).ToArray());
        Assert.IsTrue(consumed.All(m => Equals(m.Tag("error.type"), "System.InvalidOperationException")));
        Assert.HasCount(2, metrics.For(ProcessDuration, Type));
    }

    [TestMethod]
    public async Task Reports_an_unknown_type_as_dead()
    {
        using var metrics = new MetricRecorder();
        await StoreAsync(Envelope() with { Type = "not-registered" });

        await ProcessAsync();

        var consumed = metrics.For(ConsumedMessages, "not-registered").Single();
        Assert.AreEqual("dead", consumed.Tag("messagingkit.outcome"));
        Assert.AreEqual("unknown_message_type", consumed.Tag("error.type"));
    }

    [TestMethod]
    public async Task Counts_stored_and_duplicate_deliveries_to_the_inbox()
    {
        using var metrics = new MetricRecorder();
        var envelope = Envelope();

        await StoreAsync(envelope);
        await StoreAsync(envelope);

        CollectionAssert.AreEqual(
            new[] { "stored", "duplicate" },
            metrics.For(ReceivedMessages, Type).Select(m => m.Tag("messagingkit.outcome")).ToArray());
    }

    private static MessageEnvelope Envelope() => new()
    {
        Id = Guid.CreateVersion7(),
        Type = Type,
        Payload = """{"to":"ada@example.com","subject":"Hello"}""",
        CreatedAt = TestHost.Clock.GetUtcNow(),
    };

    private static Task<bool> StoreAsync(MessageEnvelope envelope) =>
        WithScopeAsync(services => services.GetRequiredService<IInbox>().TryStoreAsync(envelope));

    private static Task<int> DispatchAsync() =>
        TestHost.Services.GetRequiredService<OutboxDispatcher>().RunOnceAsync(CancellationToken.None);

    private static Task<int> ProcessAsync() =>
        TestHost.Services.GetRequiredService<InboxProcessor>().RunOnceAsync(CancellationToken.None);
}
