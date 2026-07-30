using EmailService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestingKit;
using TestingKit.Postgres;
using TestingKit.Smtp;

namespace EmailService.IntegrationTests;

[TestClass]
public static class TestHost
{
    public const string AdminApiKey = "test-admin-key";
    public const string SenderApiKey = "test-sender-key";

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
            .AddSetting("Smtp:Host", () => Smtp.Host)
            .AddSetting("Smtp:Port", () => Smtp.SmtpPort.ToString())
            .AddSetting("Smtp:Security", () => "None")
            .AddSetting("EmailDefaults:FromAddress", () => "no-reply@example.com")
            .AddSetting("EmailDefaults:FromName", () => "Example")
            .AddSetting("ApiKeys:Keys:admin:Key", () => AdminApiKey)
            .AddSetting("ApiKeys:Keys:admin:IsAdmin", () => "true")
            .AddSetting("ApiKeys:Keys:sender:Key", () => SenderApiKey)
            .AddSetting("ApiKeys:Keys:sender:IsAdmin", () => "false");

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
