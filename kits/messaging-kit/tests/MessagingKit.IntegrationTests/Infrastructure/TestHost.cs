using MessagingKit.TestSupport;
using MessagingKit.Inbox;
using MessagingKit.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TestingKit;
using TestingKit.Postgres;

namespace MessagingKit.IntegrationTests.Infrastructure;

[TestClass]
public static class TestHost
{
    public static TestEnvironment Environment { get; } = new();

    public static PostgresFixture Postgres { get; } = new(
        clientOptions: new PostgresClientOptions { SchemasToInclude = { "messaging", "billing" } });

    public static RecordingTransport Transport { get; } = new();

    public static HandlerLog Handlers { get; } = new();

    public static FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    public static ServiceProvider Services { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        Environment.AddFixture(Postgres);
        await Environment.StartAsync(context.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Transport);
        services.AddSingleton(Handlers);
        services.AddScoped<IMessageTransport>(sp => sp.GetRequiredService<RecordingTransport>());

        services.AddDbContext<TestDbContext>(builder => builder.UseNpgsql(Postgres.ConnectionString));

        services.AddOutbox<TestDbContext>().AddMessage<SendEmail>("send-email");
        services.AddInbox<TestDbContext>().AddHandler<SendEmail, RecordingHandler>("send-email");

        Services = services.BuildServiceProvider();

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync(context.CancellationToken);
        }

        await Postgres.SnapshotAsync(context.CancellationToken);
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        await Services.DisposeAsync();
        await Environment.DisposeAsync();
    }
}
