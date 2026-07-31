using MailingKit;
using MailingKit.Persistence;
using MailingKit.Smtp;
using MessagingKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestingKit;
using TestingKit.Postgres;
using TestingKit.Smtp;

namespace MailingKit.IntegrationTests;

/// <summary>
/// A host wired the way a product would wire one: MessagingKit for durability, MailingKit for the
/// handler, real Postgres and a real SMTP server. Nothing here is faked, so what the tests exercise
/// is what ships.
/// </summary>
[TestClass]
public static class TestHost
{
    public static TestEnvironment Environment { get; } = new();

    public static PostgresFixture Postgres { get; } = new(
        containerOptions: new PostgresContainerOptions { Database = "mailingkit" },
        clientOptions: new PostgresClientOptions { SchemasToInclude = { "email", "messaging" } });

    public static SmtpFixture Smtp { get; } = new();

    public static ServiceProvider Services { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        Environment.AddFixture(Postgres);
        Environment.AddFixture(Smtp);

        await Environment.StartAsync(context.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(b => b.UseNpgsql(Postgres.ConnectionString));

        services.AddMessaging<AppDbContext>();

        services.AddMailing<AppDbContext>(o =>
        {
            o.Templates.UseDatabase();
            o.Defaults.FromAddress = "no-reply@example.com";
            o.Defaults.FromName = "Example";
        });

        services.AddSmtpTransport(o =>
        {
            o.Host = Smtp.Host;
            o.Port = Smtp.SmtpPort;
            o.Security = MailKit.Security.SecureSocketOptions.None;
        });

        Services = services.BuildServiceProvider();

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

/// <summary>Stands in for a product's own context, owning both kits' tables.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.AddMessaging();        // messaging.outbox + messaging.inbox
        modelBuilder.AddMailing();          // email.email_log
        modelBuilder.AddEmailTemplates();   // email.templates
    }
}
