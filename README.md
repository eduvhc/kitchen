# TestingKit

Reusable integration-test fixtures for .NET 10, built on [Testcontainers](https://dotnet.testcontainers.org/). Start real infrastructure per test run, publish its connection details into your app's configuration, and reset state between tests.

Built for net10.0 with central package management, one package per dependency, a shared reset contract, and Postgres + SMTP fixtures.

## Packages

| Package | Contents |
| --- | --- |
| `TestingKit` | `ITestFixture`, `IResettableFixture`, `TestFixtureBase<,>`, `TestEnvironment` |
| `TestingKit.Postgres` | `PostgresFixture` — Postgres container, SQL helpers, Respawn reset |
| `TestingKit.SqlServer` | `SqlServerFixture` — SQL Server container, SQL helpers, Respawn reset |
| `TestingKit.Smtp` | `SmtpFixture` — Mailpit container, inbox polling and assertions |
| `TestingKit.Azurite` | `AzuriteFixture` — blob storage emulator |
| `TestingKit.RabbitMq` | `RabbitMqFixture` — publish, consume, purge |
| `TestingKit.EntityFramework` | EF Core migration helpers for any fixture |
| `TestingKit.AspNetCore` | `TestingKitWebApplicationFactory<TEntryPoint>` |
| `TestingKit.MSTest` | `IntegrationTest` base class with per-test reset |

Each infrastructure package pulls only its own client library, so referencing `TestingKit.Postgres` does not drag in Azure Storage or RabbitMQ.

## Quick start (MSTest)

```csharp
[TestClass]
public static class TestHost
{
    public static TestEnvironment Environment { get; } = new();

    public static PostgresFixture Postgres { get; } = new(
        clientOptions: new PostgresClientOptions { SchemasToInclude = { "public" } });

    public static SmtpFixture Smtp { get; } = new();

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        Environment.AddFixture(Postgres);
        Environment.AddFixture(Smtp);
        Environment.AddSetting("ConnectionStrings:Postgres", () => Postgres.ConnectionString);
        Environment.AddSetting("Smtp:Port", () => Smtp.SmtpPort.ToString());

        await Environment.StartAsync(context.CancellationToken);
        await MigrateYourSchemaAsync();
        await Postgres.SnapshotAsync(context.CancellationToken);
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync() => await Environment.DisposeAsync();
}

[TestClass]
public class OrderTests : IntegrationTest
{
    protected override TestEnvironment Environment => TestHost.Environment;

    [TestMethod]
    public async Task Writes_an_order()
    {
        await TestHost.Postgres.ExecuteSqlAsync("INSERT INTO orders (id) VALUES (1)");
        Assert.AreEqual(1, await TestHost.Postgres.CountAsync("orders"));
    }
}
```

Containers start once per assembly. `IntegrationTest` resets every resettable fixture before each test, so tests stay isolated without paying container startup each time.

## Reset model

`SnapshotAsync()` records the schema as it stands — call it after migrations and seed data. `ResetAsync()` then deletes everything written since, keeping tables, indexes, and migration history. `TestEnvironment.ResetAsync()` fans out to every fixture implementing `IResettableFixture`: Postgres and SQL Server truncate via Respawn, `SmtpFixture` empties the inbox, `AzuriteFixture` clears the containers you list, `RabbitMqFixture` purges the queues you list.

## Wiring an ASP.NET Core app

```csharp
public sealed class ApiFactory(TestEnvironment environment)
    : TestingKitWebApplicationFactory<Program>(environment);
```

Settings registered with `AddSetting` are pushed into the host with `UseSetting`, so the app binds its real configuration and only the endpoints change. Add per-test service overrides with `WithServices(...)`.

## Using external infrastructure

Every fixture accepts a `ConnectionString` in its client options. Set it and the fixture skips Docker and talks to that server instead — useful on a locked-down CI agent or when reproducing against a shared environment. `IsExternal` reports which mode is active. `SmtpFixture` additionally needs `ApiBaseAddress` to reach the mail catcher's HTTP API.

## Conventions

- `net10.0`, nullable enabled, warnings as errors, `latest-recommended` analysis.
- Central package management in `Directory.Packages.props`; no versions in project files.
- `slnx` solution, `global.json` pinned to the 10.0.3xx SDK band with `latestFeature` roll-forward.
- Versioned by `MinVer` from `v*` tags; packages build deterministically with SourceLink in CI.

## Requirements

Docker (or a Testcontainers-compatible runtime) must be available. On WSL2, Docker Desktop with WSL integration or a docker engine inside the distro both work.

## Tests

```bash
dotnet test
```

Runs the kit's own suite against real Postgres and Mailpit containers.
