# kitchen

Where the Kits live.

A family of small, independent .NET packages. Each kit owns one concern, versions on its own schedule, and can be taken without the others.

| Kit | Concern | Packages |
| --- | --- | --- |
| [TestingKit](kits/testing-kit) | Real infrastructure in tests, on Testcontainers | `TestingKit`, `.Postgres`, `.SqlServer`, `.Smtp`, `.RabbitMq`, `.Azurite`, `.Minio`, `.EntityFramework`, `.AspNetCore`, `.MSTest` |
| [MessagingKit](kits/messaging-kit) | Transactional outbox and inbox; durable messaging between modules | `MessagingKit`, `.Abstractions`, `.Outbox`, `.Inbox`, `.InProcess`, `.Testing` |
| [MailingKit](kits/mailing-kit) | Email as a module: templates, SMTP or Resend, a record of what was sent | `MailingKit`, `.Smtp`, `.Resend` |
| [ResultingKit](kits/resulting-kit) | Explicit success and failure; typed errors that say whether retrying helps | `ResultingKit`, `.AspNetCore` |
| [StoringKit](kits/storing-kit) | Object storage behind one contract: Azure Blob Storage, AWS S3, Cloudflare R2, MinIO | `StoringKit`, `.Azure`, `.S3` |
| [ValidatingKit](kits/validating-kit) | Fluent, dependency-free validation that returns `Result`s with field-level errors | `ValidatingKit` |

Each kit's own README documents its packages, and is what ships as the NuGet readme.

## The naming rule

Every kit is `<Gerund>Kit` — the *-ing* form of what it does. Testing, Messaging, Mailing, Resulting, Storing, Validating, and later Authenticating, Billing, Scheduling, Caching.

Sub-packages extend the root with a dot: `TestingKit.Postgres`, `MessagingKit.Outbox`, `MailingKit.Smtp`. **One package per external dependency**, so a host on Resend never restores MailKit, and a module that only needs contracts never restores EF Core.

## House rules

These are what stop a family of packages collapsing into one blob:

- **Kits do not reference each other for convenience.** MailingKit depends on MessagingKit because an email *is* a message, and both depend on ResultingKit because their transport contracts return its `Result`, as StoringKit's store contract does, and ValidatingKit's whole output is a `Result` — real dependencies. Anything weaker gets wired by the host instead.
- **The host owns the `DbContext`.** Kits map their tables into it with `modelBuilder.AddX()`, so migrations live in the application's own history. No kit ships migrations.
- **Abstractions ship separately.** A module referencing `MessagingKit.Abstractions` gets the contracts and the analyzer, not the EF Core machinery.
- **No `Common`, no `Shared`, no `Utils`.** They become dumping grounds. If two kits need the same thing, it earns its own kit.
- **Unit and integration suites are separate projects.** `<Kit>.UnitTests` runs with no Docker; `<Kit>.IntegrationTests` uses TestingKit fixtures against real containers.

## Layout

```
kitchen/
  Directory.Build.props       language version, nullable, analysis level — every project
  Directory.Packages.props    every dependency version, in one place
  Directory.Packaging.props   NuGet metadata and versioning, shared by every kit's src
  Directory.Tests.props       rules relaxed for test projects only
  Kitchen.slnx                every project
  kits/
    testing-kit/
      README.md               ships as the NuGet readme for TestingKit.*
      src/Directory.Build.props    imports the shared props, points at this README
      src/  tests/
    messaging-kit/
    mailing-kit/
    resulting-kit/
    storing-kit/
    validating-kit/
```

Shared configuration lives once at the root. A kit's own `src/Directory.Build.props` adds only what is specific to it: its README and its version tag prefix.

## Inside a package

Every package is organised by **vertical slice**: one folder per feature, holding everything that feature needs. Code that changes together lives together; nothing is filed by layer at the top of a package.

```
src/<Package>/
  <Slice>/                       one feature, named for what it does: Sending, Templates, Dispatching
    Abstractions/                interfaces and contract types the slice exposes or depends on
    Options/                     options classes bound from configuration
    Persistence/                 EF Core entity configurations and stores
    Diagnostics/                 metrics, tags and instrumentation decorators
    *.cs                         the slice itself: handlers, services, validators, entities, exceptions
  <Kit>Instrumentation.cs        the package's ActivitySource and Meter names
  <Package>ServiceCollectionExtensions.cs    the package's registration entry point
```

- **A slice is a feature, never a layer.** There is no `Options/`, `Abstractions/`, `Domain/` or `Persistence/` at the top of a package; each lives inside the slice it belongs to.
- **Role subfolders are fixed and optional.** A slice uses only `Abstractions/`, `Options/`, `Persistence/` and `Diagnostics/`, and only once it has something for them. Everything else, entities and enums included, sits at the slice root.
- **A package that does one thing is one slice.** A fixture, a transport, or `ResultingKit` has no slice folder; its role subfolders sit directly under the package root.
- **A type lives with the slice that owns the concept.** Another slice that needs it references it there. Only package-wide plumbing (registration, instrumentation names) sits at the package root.
- **Namespaces are `<Package>.<Slice>`.** Role subfolders add no segment, so one `using` brings in a whole feature: `IEmailSender` in `Sending/Abstractions/` is `MailingKit.Sending`. Package-root files and single-slice packages use `<Package>`.
- **Tests mirror the slices.** `tests/<Package>.UnitTests/<Slice>/` holds that slice's tests.

## Working on it

```bash
dotnet build                                                   # everything
dotnet test                                                    # everything, needs Docker
dotnet test kits/messaging-kit/tests/MessagingKit.UnitTests    # fast, no Docker
```

Kits reference each other by **project**, not package. A change to MessagingKit is visible to MailingKit on the next build — no publish, no version bump, no waiting for nuget.org to index. That round trip is the main reason these live together.

## Versioning and releases

The family ships as **one version**, driven by [release-please](https://github.com/googleapis/release-please) and stamped by [MinVer](https://github.com/adamralph/minver). A conventional commit opens a release PR; merging it tags `v0.3.0` and publishes every package at `0.3.0`.

Independent per-kit versions were tried and do not work here. Kits reference each other by project, so MailingKit's packed dependency is whatever MessagingKit stamps at build time. With separate version lines that is a prerelease unless both kits happen to be tagged on the same commit, and NuGet refuses to let a stable package depend on a prerelease (NU5104). One version removes the coordination problem entirely.

The cost is that TestingKit gets a version bump when only MailingKit changed. That is cosmetic, and honest: MailingKit cannot move without MessagingKit anyway.

Publishing goes to nuget.org via trusted publishing, so there are no API keys anywhere. The job is skipped unless the `NUGET_USER` repository variable is set and a trusted-publishing policy names this repository, its `publish.yml`, and the `nuget` environment.

## Adding a kit

1. Name it `<Gerund>Kit`.
2. Create `kits/<name>-kit/` with `README.md`, `src/`, `tests/`.
3. Copy `src/Directory.Build.props` from a sibling — it is two imports and the kit's README.
4. Add it to `Kitchen.slnx`. Versioning and publishing need no changes — the whole repo ships together.
5. Split the suites from the start: `<Kit>.UnitTests` and `<Kit>.IntegrationTests`.

Nothing else — CI, publishing, and dependency versions are already shared.
