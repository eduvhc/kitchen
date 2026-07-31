using MessagingKit.Outbox;
using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using MessagingKit.Testing;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.IntegrationTests;

[TestClass]
public class TransportRoutingTests : MessagingTest
{
    [TestMethod]
    public async Task Routes_by_destination_ahead_of_message_type()
    {
        var byType = new RecordingTransport();

        await using var host = Build(
            services => services.AddSingleton(byType),
            messaging => messaging
                .UseTransport<RecordingTransport>("send-email")
                .UseTransport<DestinationTransport>("email-module"));

        await StageAsync(host, destination: "email-module");
        await host.DrainOutboxAsync();

        Assert.IsEmpty(byType.Sent, "a destination rule should win over the message type rule");
        Assert.HasCount(1, DestinationTransport.Sent);
    }

    [TestMethod]
    public async Task Falls_back_to_the_message_type_when_no_destination_matches()
    {
        var byType = new RecordingTransport();

        await using var host = Build(
            services => services.AddSingleton(byType),
            messaging => messaging.UseTransport<RecordingTransport>("send-email"));

        await StageAsync(host, destination: "nothing-routes-this");
        await host.DrainOutboxAsync();

        Assert.HasCount(1, byType.Sent);
    }

    [TestMethod]
    public async Task Falls_back_to_the_default_transport()
    {
        var fallback = new RecordingTransport();

        await using var host = Build(
            services => services.AddSingleton(fallback),
            messaging => messaging.UseTransport<RecordingTransport>());

        await StageAsync(host);
        await host.DrainOutboxAsync();

        Assert.HasCount(1, fallback.Sent);
    }

    [TestMethod]
    public async Task Keeps_named_types_in_process_when_another_transport_is_the_default()
    {
        var broker = new RecordingTransport();
        var log = new HandlerLog();

        await using var host = Build(
            services => services.AddSingleton(broker).AddSingleton(log),
            messaging => messaging
                .UseInProcessTransport("send-email")
                .UseTransport<RecordingTransport>());

        await StageAsync(host);
        await host.DrainMessagingAsync(ct: CancellationToken.None);

        Assert.IsEmpty(broker.Sent, "the named type should have stayed in process");
        Assert.HasCount(1, log.Handled);
    }

    [TestMethod]
    public async Task Dead_letters_a_message_with_no_route_at_all()
    {
        // Built à la carte so nothing supplies a default transport or a bare IMessageTransport.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TestHost.Clock);
        services.AddDbContext<TestDbContext>(b => b.UseNpgsql(TestHost.Postgres.ConnectionString));
        services.AddOutbox<TestDbContext>()
            .AddMessage<SendEmail>()
            .UseTransport<DestinationTransport>("a-type-we-never-send");

        await using var host = services.BuildServiceProvider();

        await StageAsync(host);
        await host.DrainOutboxAsync();

        var stored = await StoredAsync(host);

        Assert.AreEqual(OutboxStatus.Dead, stored.Status, "a wiring gap should not be retried forever");
        Assert.AreEqual(1, stored.AttemptCount);
        StringAssert.Contains(stored.LastError, "No transport is registered");
    }

    /// <summary>Records separately from <see cref="RecordingTransport"/> so routing can be told apart.</summary>
    private sealed class DestinationTransport : IMessageTransport
    {
        public static List<MessageEnvelope> Sent { get; } = [];

        public Task<TransportResult> SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            Sent.Add(envelope);
            return Task.FromResult(TransportResult.Ok());
        }
    }

    [TestInitialize]
    public void ClearDestinationTransport() => DestinationTransport.Sent.Clear();

    private static ServiceProvider Build(
        Action<IServiceCollection> register,
        Action<MessagingBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TestHost.Clock);
        services.AddDbContext<TestDbContext>(b => b.UseNpgsql(TestHost.Postgres.ConnectionString));

        register(services);

        var messaging = services.AddMessaging<TestDbContext>();
        messaging.Handles<SendEmail, RecordingHandler>();
        configure(messaging);

        services.TryAddHandlerLog();

        return services.BuildServiceProvider();
    }

    private static async Task StageAsync(IServiceProvider host, string? destination = null)
    {
        await using var scope = host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        scope.ServiceProvider.GetRequiredService<IOutbox>()
            .Add(new SendEmail("ada@example.com", "Hello"), destination);

        await db.SaveChangesAsync();
    }

    private static async Task<OutboxMessage> StoredAsync(IServiceProvider host)
    {
        await using var scope = host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync();
    }
}

internal static class HandlerLogRegistration
{
    public static void TryAddHandlerLog(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(HandlerLog)))
        {
            services.AddSingleton<HandlerLog>();
        }
    }
}
