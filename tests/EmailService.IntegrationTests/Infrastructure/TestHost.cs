using EmailService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestingKit.Postgres;
using TestingKit.Smtp;
using TestingKit;

namespace EmailService.IntegrationTests.Infrastructure;

[TestClass]
public static class TestHost
{
    public static TestEnvironment Environment { get; } = new();

    public static PostgresFixture Postgres { get; } = new(
        containerOptions: new PostgresContainerOptions { Database = "emailservice" },
        clientOptions: new PostgresClientOptions
        {
            SchemasToInclude = { "email" },
            TablesToIgnore = { "email.__migrations" },
        });

    public static SmtpFixture Smtp { get; } = new();

    public static EmailServiceFactory Factory { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        Environment.AddFixture(Postgres);
        Environment.AddFixture(Smtp);

        Environment
            .AddSetting("ConnectionStrings:Postgres", () => Postgres.ConnectionString)
            .AddSetting("Database:MigrateOnStartup", () => "false")
            .AddSetting("Dispatcher:Enabled", () => "false")
            .AddSetting("Inbox:Enabled", () => "false")
            .AddSetting("Inbox:Schema", () => "email")
            .AddSetting("Smtp:Host", () => Smtp.Host)
            .AddSetting("Smtp:Port", () => Smtp.SmtpPort.ToString())
            .AddSetting("Smtp:Security", () => "None")
            .AddSetting("EmailDefaults:FromAddress", () => "no-reply@example.com")
            .AddSetting("EmailDefaults:FromName", () => "Example")
            .AddSetting("RateLimit:PermitLimit", () => "10000")
            .AddSetting("RateLimit:Sources:limited-source:PermitLimit", () => "3")
            .AddSetting("RateLimit:Sources:limited-source:WindowSeconds", () => "300");

        await Environment.StartAsync(context.CancellationToken);

        Factory = new EmailServiceFactory(Environment);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmailDbContext>();
            await db.Database.MigrateAsync(context.CancellationToken);
        }

        await Postgres.SnapshotAsync(context.CancellationToken);
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        await Factory.DisposeAsync();
        await Environment.DisposeAsync();
    }
}
