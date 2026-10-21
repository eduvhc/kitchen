using TestingKit;
using TestingKit.Azurite;
using TestingKit.Postgres;

namespace LockingKit.IntegrationTests;

/// <summary>One Azurite and one PostgreSQL for the contract suites, started once for the assembly.</summary>
[TestClass]
public static class TestHost
{
    public const string Container = "locks";

    public static TestEnvironment Environment { get; } = new();

    public static AzuriteFixture Azurite { get; } = new();

    public static PostgresFixture Postgres { get; } = new();

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        Environment.AddFixture(Azurite);
        Environment.AddFixture(Postgres);

        await Environment.StartAsync(context.CancellationToken);
        await Azurite.BlobClient.GetBlobContainerClient(Container).CreateIfNotExistsAsync(cancellationToken: context.CancellationToken);
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync() => await Environment.DisposeAsync();
}
