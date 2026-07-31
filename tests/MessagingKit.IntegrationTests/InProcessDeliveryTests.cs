using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using MessagingKit.Testing;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.IntegrationTests;

/// <summary>
/// Exercises the full seam — outbox, transport, inbox, handler — against a real database, using its
/// own container so the wiring under test is the wiring being asserted.
/// </summary>
[TestClass]
public class InProcessDeliveryTests : MessagingTest
{
    [TestMethod]
    public async Task Carries_a_message_from_the_outbox_to_the_handler()
    {
        var log = new HandlerLog();
        await using var host = BuildHost(log);

        await StageAsync(host, new SendEmail("ada@example.com", "Hello"));

        await host.DrainMessagingAsync(ct: CancellationToken.None);

        var handled = log.Handled.Single();
        Assert.AreEqual("ada@example.com", handled.Message.To);
        Assert.AreEqual("send-email", handled.Context.Type);
    }

    [TestMethod]
    public async Task Marks_the_outbox_row_sent_once_the_inbox_has_it()
    {
        var log = new HandlerLog();
        await using var host = BuildHost(log);

        await StageAsync(host, new SendEmail("ada@example.com", "Hello"));

        await host.DrainOutboxAsync();

        var stored = await StoredAsync(host);
        Assert.AreEqual(OutboxStatus.Sent, stored.Status);

        // Delivered but not yet handled — the two halves are genuinely separate.
        Assert.IsEmpty(log.Handled);
    }

    [TestMethod]
    public async Task Redelivery_of_the_same_message_is_handled_once()
    {
        var log = new HandlerLog();
        await using var host = BuildHost(log);

        var envelope = new MessageEnvelope
        {
            Id = Guid.CreateVersion7(),
            Type = "send-email",
            Payload = """{"to":"ada@example.com","subject":"Hello"}""",
            CreatedAt = TestHost.Clock.GetUtcNow(),
        };

        await using (var scope = host.CreateAsyncScope())
        {
            var transport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();

            Assert.IsTrue((await transport.SendAsync(envelope)).Success);
            Assert.IsTrue((await transport.SendAsync(envelope)).Success, "a duplicate is a successful delivery");
        }

        await host.DrainMessagingAsync(ct: CancellationToken.None);

        Assert.HasCount(1, log.Handled);
    }

    [TestMethod]
    public async Task Wakes_the_inbox_processor_on_delivery()
    {
        var log = new HandlerLog();
        await using var host = BuildHost(log);

        var signal = host.GetRequiredService<MessagingKit.Inbox.InboxSignal>();
        await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);

        await StageAsync(host, new SendEmail("ada@example.com", "Hello"));
        await host.DrainOutboxAsync();

        Assert.IsTrue(
            await signal.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None),
            "storing to the inbox should wake the processor rather than wait out the poll interval");
    }

    [TestMethod]
    public async Task Routes_a_type_to_its_own_transport_and_leaves_the_rest_in_process()
    {
        var log = new HandlerLog();
        var recording = new RecordingTransport();

        await using var host = BuildHost(log, messaging => messaging
            .UseTransportFor<RecordingTransport, SendEmail>(),
            services => services.AddSingleton(recording));

        await StageAsync(host, new SendEmail("ada@example.com", "Hello"));

        await host.DrainMessagingAsync(ct: CancellationToken.None);

        Assert.HasCount(1, recording.Sent);
        Assert.IsEmpty(log.Handled, "routing to another transport should bypass the local inbox");
    }

    private static ServiceProvider BuildHost(
        HandlerLog log,
        Action<MessagingBuilder>? configure = null,
        Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TestHost.Clock);
        services.AddSingleton(log);
        services.AddDbContext<TestDbContext>(b => b.UseNpgsql(TestHost.Postgres.ConnectionString));

        register?.Invoke(services);

        var messaging = services.AddMessaging<TestDbContext>();
        messaging.Handles<SendEmail, RecordingHandler>();
        configure?.Invoke(messaging);

        return services.BuildServiceProvider();
    }

    private static async Task StageAsync(IServiceProvider host, SendEmail message)
    {
        await using var scope = host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        scope.ServiceProvider.GetRequiredService<IOutbox>().Add(message);
        await db.SaveChangesAsync();
    }

    private static async Task<OutboxMessage> StoredAsync(IServiceProvider host)
    {
        await using var scope = host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync();
    }
}
