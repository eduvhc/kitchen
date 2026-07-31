# kitchen

Where the Kits live.

A family of small, independent .NET packages. Each kit owns one concern, versions on its own schedule, and can be taken without the others.

| Kit | Concern | Packages |
| --- | --- | --- |
| [TestingKit](kits/testing-kit) | Real infrastructure in tests, on Testcontainers | `TestingKit`, `.Postgres`, `.SqlServer`, `.Smtp`, `.RabbitMq`, `.Azurite`, `.EntityFramework`, `.AspNetCore`, `.MSTest` |
| [MessagingKit](kits/messaging-kit) | Transactional outbox and inbox; durable messaging between modules | `MessagingKit`, `.Abstractions`, `.Outbox`, `.Inbox`, `.InProcess`, `.Testing` |
| [MailingKit](kits/mailing-kit) | Email as a module: templates, SMTP, a record of what was sent | `MailingKit`, `.Smtp` |

Each kit's own README documents its packages, and is what ships as the NuGet readme.

## The naming rule

Every kit is `<Gerund>Kit` — the *-ing* form of what it does. Testing, Messaging, Mailing, and later Authenticating, Billing, Scheduling, Caching.

Sub-packages extend the root with a dot: `TestingKit.Postgres`, `MessagingKit.Outbox`, `MailingKit.Smtp`. **One package per external dependency**, so a host on Resend never restores MailKit, and a module that only needs contracts never restores EF Core.

## House rules

These are what stop a family of packages collapsing into one blob:

- **Kits do not reference each other for convenience.** MailingKit depends on MessagingKit because an email *is* a message — a real dependency. Anything weaker gets wired by the host instead.
- **The host owns the `DbContext`.** Kits map their tables into it with `modelBuilder.AddX()`, so migrations live in the application's own history. No kit ships migrations.
- **Abstractions ship separately.** A module referencing `MessagingKit.Abstractions` gets the contracts and the analyzer, not the EF Core machinery.
- **No `Common`, no `Shared`, no `Utils`.** They become dumping grounds. If two kits need the same thing, it earns its own kit.
- **Unit and integration suites are separate projects.** `<Kit>.UnitTests` runs with no Docker; `<Kit>.IntegrationTests` uses TestingKit fixtures against real containers.

## Layout

```
kitchen/
  Directory.Build.props       language version, nullable, analysis level — every project
  Directory.Packages.props    every dependency version, in one place
  Directory.Tests.props       rules relaxed for test projects only
  Kitchen.slnx                every project
  kits/
    testing-kit/
      README.md               ships as the NuGet readme for TestingKit.*
      src/Directory.Build.props    package metadata, MinVer tag prefix
      src/  tests/
    messaging-kit/
    mailing-kit/
```

Shared configuration lives once at the root. A kit's own `src/Directory.Build.props` adds only what is specific to it: its README and its version tag prefix.

## Working on it

```bash
dotnet build                                                   # everything
dotnet test                                                    # everything, needs Docker
dotnet test kits/messaging-kit/tests/MessagingKit.UnitTests    # fast, no Docker
```

Kits reference each other by **project**, not package. A change to MessagingKit is visible to MailingKit on the next build — no publish, no version bump, no waiting for nuget.org to index. That round trip is the main reason these live together.

## Versioning and releases

Each kit versions independently, driven by [release-please](https://github.com/googleapis/release-please) in manifest mode and stamped by [MinVer](https://github.com/adamralph/minver).

- A conventional commit touching `kits/messaging-kit/**` opens a release PR for **that kit only**.
- Merging it tags `messaging-kit-v0.3.0` and publishes only `MessagingKit.*`.
- MinVer reads the matching tag prefix, so kits never inherit each other's version numbers.

Publishing goes to nuget.org (trusted publishing, no API keys) and GitHub Packages. The nuget.org job is skipped unless the `NUGET_USER` repository variable is set and a trusted-publishing policy exists for this repository.

## Adding a kit

1. Name it `<Gerund>Kit`.
2. Create `kits/<name>-kit/` with `README.md`, `src/`, `tests/`.
3. Copy `src/Directory.Build.props` from a sibling and change `MinVerTagPrefix` to `<name>-kit-v`.
4. Add it to `release-please-config.json`, `.release-please-manifest.json` (at `0.0.0`), and `Kitchen.slnx`.
5. Split the suites from the start: `<Kit>.UnitTests` and `<Kit>.IntegrationTests`.

Nothing else — CI, publishing, and dependency versions are already shared.
