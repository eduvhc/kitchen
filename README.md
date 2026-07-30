# TestingKit

Reusable integration-test fixtures for .NET 10, built on [Testcontainers](https://dotnet.testcontainers.org/). Start real infrastructure per test run, publish its connection details into your app's configuration, and reset state between tests.

Built for net10.0 with central package management, one package per dependency, a shared reset contract, and Postgres + SMTP fixtures.

## Install

```bash
dotnet add package TestingKit.Postgres
dotnet add package TestingKit.Smtp
dotnet add package TestingKit.MSTest
```

Packages are published to nuget.org and to GitHub Packages. To use the GitHub Packages feed, add it once:

```bash
dotnet nuget add source https://nuget.pkg.github.com/eduvhc/index.json \
  --name testing-kit --username <your-github-user> --password <a-classic-PAT-with-read:packages>
```

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

## Releasing

Versioning and publishing are automated; nothing is hand-edited.

1. Land commits on `main` using [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `perf:`, `deps:`).
2. `release-please` keeps an open release PR with the next SemVer version and the generated `CHANGELOG.md`.
3. Merging that PR creates the GitHub Release and the `v*` tag.
4. The same workflow then calls `publish.yml`, which builds, tests, packs (MinVer stamps the version from the tag), attests build provenance, and pushes to nuget.org and GitHub Packages.

`publish.yml` is also reachable directly: push a `v*` tag by hand, or run it from the Actions tab against any ref.

nuget.org authentication uses [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) — no API key is stored. It needs three one-time setup steps:

- On nuget.org, add a trusted publishing policy for this repository, workflow file `publish.yml`, and environment `nuget`.
- In repository settings, create the `nuget` environment (add required reviewers if you want a manual gate before a push to nuget.org).
- Set the repository variable `NUGET_USER` to your nuget.org account name.

Until those exist the `nuget-org` job fails and the `github-packages` job still succeeds, so the packages remain installable from GitHub.

Dependency updates are grouped weekly by Dependabot with a cooldown, so a freshly published upstream version has to age before it lands in a PR.

## Conventions

- `net10.0`, nullable enabled, warnings as errors, `latest-recommended` analysis.
- Central package management in `Directory.Packages.props`; no versions in project files.
- `slnx` solution, `global.json` pinned to the 10.0.3xx SDK band with `latestFeature` roll-forward.
- Versioned by `MinVer` from `v*` tags, floored at `0.1`; packages build deterministically with SourceLink and ship symbols.
- Releases driven by `release-please`; dependencies by Dependabot (grouped, with cooldown).
- Workflows pinned to current majors: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7`, `NuGet/login@v1`, `actions/attest-build-provenance@v4`.

## Requirements

Docker (or a Testcontainers-compatible runtime) must be available. On WSL2, Docker Desktop with WSL integration or a docker engine inside the distro both work.

## Tests

```bash
dotnet test
```

Runs the kit's own suite against real Postgres and Mailpit containers.
