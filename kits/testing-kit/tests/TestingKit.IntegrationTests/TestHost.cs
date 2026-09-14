using TestingKit.Postgres;
using TestingKit.RabbitMq;
using TestingKit.Smtp;

namespace TestingKit.IntegrationTests;

[TestClass]
public static class TestHost
{
    public static TestEnvironment Environment { get; } = new();

    public static PostgresFixture Postgres { get; } = new(
        clientOptions: new PostgresClientOptions
        {
            SetupScripts =
            {
                """
                CREATE TABLE products (
                    id   SERIAL PRIMARY KEY,
                    name TEXT NOT NULL
                );
                """,
            },
            SchemasToInclude = { "public" },
        });

    public static SmtpFixture Smtp { get; } = new();

    public const string Exchange = "testingkit.exchange";

    public const string Queue = "testingkit.queue";

    public static RabbitMqFixture RabbitMq { get; } = new(
        clientOptions: new RabbitMqClientOptions
        {
            ReuseConnection = true,
            QueuesToPurge = { Queue },
        });

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        Environment
            .AddFixture(Postgres);

        Environment
            .AddFixture(Smtp);

        Environment
            .AddFixture(RabbitMq);

        Environment.AddSetting("ConnectionStrings:Postgres", () => Postgres.ConnectionString);
        Environment.AddSetting("ConnectionStrings:RabbitMq", () => RabbitMq.ConnectionString);
        Environment.AddSetting("Smtp:Port", () => Smtp.SmtpPort.ToString());

        await Environment.StartAsync(context.CancellationToken);
        await Postgres.SnapshotAsync(context.CancellationToken);
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync() => await Environment.DisposeAsync();
}
