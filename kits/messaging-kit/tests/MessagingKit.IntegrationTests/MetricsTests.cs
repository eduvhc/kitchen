using System.Diagnostics.Metrics;
using MessagingKit.Abstractions;
using MessagingKit.Inbox;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.Outbox;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
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
        using var sent = Collect<long>(SentMessages);
        using var duration = Collect<double>(SendDuration);
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Hello"));

        await DispatchAsync();

        var measurement = For(sent, Type).Single();
        Assert.AreEqual(1, measurement.Value);
        Assert.AreEqual("{message}", sent.Instrument!.Unit);
        Assert.AreEqual("messagingkit", measurement.Tags["messaging.system"]);
        Assert.AreEqual("send", measurement.Tags["messaging.operation.name"]);
        Assert.AreEqual("send", measurement.Tags["messaging.operation.type"]);
        Assert.AreEqual("sent", measurement.Tags["messagingkit.outcome"]);
        Assert.IsFalse(measurement.Tags.ContainsKey("error.type"), "a success must not carry error.type");
        Assert.IsFalse(measurement.Tags.ContainsKey("messaging.message.type"), "not a semantic-convention attribute");

        var timed = For(duration, Type).Single();
        Assert.AreEqual("s", duration.Instrument!.Unit);
        Assert.IsGreaterThan(0d, timed.Value);
        Assert.AreEqual("sent", timed.Tags["messagingkit.outcome"]);
    }

    [TestMethod]
    public async Task Reports_a_transient_send_failure_as_retried()
    {
        using var sent = Collect<long>(SentMessages);
        using var duration = Collect<double>(SendDuration);
        TestHost.Transport.Behaviour = _ => Error.Transient("broker.unreachable", "broker unreachable");
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Retry me"));

        await DispatchAsync();

        var measurement = For(sent, Type).Single();
        Assert.AreEqual("retried", measurement.Tags["messagingkit.outcome"]);
        Assert.AreEqual("broker.unreachable", measurement.Tags["error.type"]);
        Assert.AreEqual("retried", For(duration, Type).Single().Tags["messagingkit.outcome"]);
    }

    [TestMethod]
    public async Task Reports_a_permanent_send_failure_as_dead()
    {
        using var sent = Collect<long>(SentMessages);
        TestHost.Transport.Behaviour = _ => Error.Failure("broker.unknown_destination", "unknown destination");
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Doomed"));

        await DispatchAsync();

        var measurement = For(sent, Type).Single();
        Assert.AreEqual("dead", measurement.Tags["messagingkit.outcome"]);
        Assert.AreEqual("broker.unknown_destination", measurement.Tags["error.type"]);
    }

    [TestMethod]
    public async Task Reports_the_last_send_attempt_as_dead()
    {
        using var sent = Collect<long>(SentMessages);
        using var duration = Collect<double>(SendDuration);
        TestHost.Transport.Behaviour = _ => Error.Transient("broker.unreachable", "still down");
        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Persistent"));
        await WithDbAsync(db => db.Set<OutboxMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxAttempts, 2)));

        await DispatchAsync();
        TestHost.Clock.Advance(TimeSpan.FromHours(1));
        await DispatchAsync();

        CollectionAssert.AreEqual(
            new[] { "retried", "dead" },
            For(sent, Type).Select(m => m.Tags["messagingkit.outcome"]).ToArray());
        Assert.HasCount(2, For(duration, Type));
    }

    [TestMethod]
    public async Task Counts_a_processed_message_and_times_the_handler()
    {
        using var consumed = Collect<long>(ConsumedMessages);
        using var duration = Collect<double>(ProcessDuration);
        await StoreAsync(Envelope());

        await ProcessAsync();

        var measurement = For(consumed, Type).Single();
        Assert.AreEqual(1, measurement.Value);
        Assert.AreEqual("{message}", consumed.Instrument!.Unit);
        Assert.AreEqual("messagingkit", measurement.Tags["messaging.system"]);
        Assert.AreEqual("process", measurement.Tags["messaging.operation.name"]);
        Assert.AreEqual("process", measurement.Tags["messaging.operation.type"]);
        Assert.AreEqual("processed", measurement.Tags["messagingkit.outcome"]);
        Assert.IsFalse(measurement.Tags.ContainsKey("error.type"), "a success must not carry error.type");

        var timed = For(duration, Type).Single();
        Assert.AreEqual("s", duration.Instrument!.Unit);
        Assert.IsGreaterThan(0d, timed.Value);
        Assert.AreEqual("processed", timed.Tags["messagingkit.outcome"]);
    }

    [TestMethod]
    public async Task Reports_a_failing_handler_as_retried_then_dead()
    {
        using var consumed = Collect<long>(ConsumedMessages);
        using var duration = Collect<double>(ProcessDuration);
        TestHost.Handlers.Behaviour = _ => throw new InvalidOperationException("downstream down");
        await StoreAsync(Envelope());
        await WithDbAsync(db => db.Set<InboxMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxAttempts, 2)));

        await ProcessAsync();
        TestHost.Clock.Advance(TimeSpan.FromHours(1));
        await ProcessAsync();

        var measurements = For(consumed, Type);
        CollectionAssert.AreEqual(
            new[] { "retried", "dead" },
            measurements.Select(m => m.Tags["messagingkit.outcome"]).ToArray());
        Assert.IsTrue(measurements.All(m => Equals(m.Tags["error.type"], "System.InvalidOperationException")));
        Assert.HasCount(2, For(duration, Type));
    }

    [TestMethod]
    public async Task Reports_an_unknown_type_as_dead()
    {
        using var consumed = Collect<long>(ConsumedMessages);
        await StoreAsync(Envelope() with { Type = "not-registered" });

        await ProcessAsync();

        var measurement = For(consumed, "not-registered").Single();
        Assert.AreEqual("dead", measurement.Tags["messagingkit.outcome"]);
        Assert.AreEqual("unknown_message_type", measurement.Tags["error.type"]);
    }

    [TestMethod]
    public async Task Counts_stored_and_duplicate_deliveries_to_the_inbox()
    {
        using var received = Collect<long>(ReceivedMessages);
        var envelope = Envelope();

        await StoreAsync(envelope);
        await StoreAsync(envelope);

        CollectionAssert.AreEqual(
            new[] { "stored", "duplicate" },
            For(received, Type).Select(m => m.Tags["messagingkit.outcome"]).ToArray());
    }

    // Scoped to this host's meter factory, so nothing another provider records can leak in.
    private static MetricCollector<T> Collect<T>(string instrument)
        where T : struct =>
        new(TestHost.Services.GetRequiredService<IMeterFactory>(), MessagingKitInstrumentation.MeterName, instrument);

    private static List<CollectedMeasurement<T>> For<T>(MetricCollector<T> collector, string messageType)
        where T : struct =>
        collector.GetMeasurementSnapshot()
            .Where(m => Equals(m.Tags.GetValueOrDefault("messagingkit.message.type"), messageType))
            .ToList();

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
