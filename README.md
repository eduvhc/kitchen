# EmailService

A generic email microservice: one HTTP call queues an email, a background dispatcher delivers it, and every attempt is recorded. Built on .NET 10 minimal APIs with a Postgres-backed durable queue, MailKit SMTP transport, and Scriban templates stored in the database.

No broker, no Redis, no cron. The queue is a Postgres table claimed with `SELECT ... FOR UPDATE SKIP LOCKED`, so scaling out means running more replicas and nothing else.

| | |
| --- | --- |
| Runtime | .NET 10, ASP.NET Core minimal APIs |
| Storage | Postgres 17 (EF Core 10, `email` schema) |
| Transport | SMTP via MailKit — swap in Resend/SES by implementing one interface |
| Templates | Scriban, stored in Postgres, editable at runtime |
| Auth | None — the service trusts its network; see [Deployment](#deployment-notes) |
| Tests | MSTest — unit with fakes, integration on real containers |

## Quick start

```bash
docker compose up -d postgres mailpit
dotnet run --project src/EmailService
```

Postgres listens on `5433`, Mailpit on `1026` (SMTP) and `8026` (web UI), chosen to stay clear of other local stacks. Development runs apply migrations at startup.

```bash
curl -X POST localhost:5294/v1/emails \
  -H 'Content-Type: application/json' \
  -d '{"to":["someone@example.com"],"subject":"Hello","html":"<p>Hello</p>"}'
```

The mail lands in Mailpit at http://localhost:8026. To run everything in containers instead: `docker compose up --build`.

## How it works

`POST /v1/emails` validates the request, renders the template if one is named, and writes a single row to `email.emails` with `status = Queued`. The response returns as soon as that row commits — the caller never waits on SMTP.

`EmailDispatcher` polls the same table. Each tick claims a batch with `FOR UPDATE SKIP LOCKED`, flips those rows to `Sending` with a lock expiry, and sends them in parallel:

- **Success** → `Sent`, with the provider message id recorded.
- **Transient failure** (connection refused, timeout, 4xx) → back to `Queued`, rescheduled with exponential backoff (30s doubling, capped at an hour).
- **Permanent failure** (5xx reply, malformed address) or attempts exhausted → `Dead`, no further tries.

A process that dies mid-send leaves rows stuck in `Sending`; the next dispatcher reclaims them once `locked_until` passes. `SKIP LOCKED` guarantees a row is claimed by exactly one worker, so every replica can run the dispatcher safely. Set `Dispatcher:Enabled=false` to run an API-only replica.

## Layout

One project, organised in vertical slices: a use case lives in one folder with its request, handler, validator, and endpoint side by side.

```
src/EmailService/
  Program.cs                       the only file in the project root

  Features/
    Emails/
      Abstractions/                IEmailQueue, EmailQueryFilter
      Domain/                      EmailMessage, EmailAttachment, EmailStatus
      Contracts/                   EmailResponse
      EmailQueue.cs                Postgres implementation (SKIP LOCKED claim)
      EmailsFeature.cs             AddEmails() + MapEmails()
      SendEmail/                   request, validator, handler, result, endpoint
      GetEmail/ ListEmails/ CancelEmail/
    Templates/
      Abstractions/                ITemplateStore
      Domain/                      EmailTemplate
      Contracts/                   TemplateResponse
      TemplateStore.cs
      TemplatesFeature.cs
      UpsertTemplate/ GetTemplate/ ListTemplates/ DeleteTemplate/ PreviewTemplate/
    Dispatch/
      EmailDispatcher.cs           BackgroundService: claim, send, retry
      DispatchFeature.cs
    Messages/
      SendEmailMessageHandler.cs   inbox handler: message to queued email
      MessagesFeature.cs
      ReceiveMessage/ GetMessage/

  Transport/
    Abstractions/                  IEmailSender, SendResult
    Smtp/SmtpEmailSender.cs
    TransportExtensions.cs
  Templating/
    Abstractions/                  ITemplateRenderer, TemplateRenderException
    ScribanTemplateRenderer.cs
    TemplatingExtensions.cs
  Persistence/                     EmailDbContext, Configurations/, Migrations/
  RateLimiting/                    per-source fixed-window limiter
  Options/                         Smtp, Dispatcher, EmailDefaults, RateLimit
  Common/                          IEndpoint, MapEndpoint<T>(), ValidationException

tests/EmailService.Tests/               unit tests, mirrors the slice folders
tests/EmailService.IntegrationTests/    container-backed tests
  Infrastructure/                       TestHost, factory, ApiTest base
  Features/                             mirrors the source slices
```

Every abstraction lives in an `Abstractions/` folder directly beside the implementation that satisfies it: `IEmailQueue` sits next to `EmailQueue`, `IEmailSender` next to `Smtp/SmtpEmailSender`. Namespaces follow folders, and each file holds one type.

`Domain/` holds entities EF maps to tables. `Contracts/` holds what crosses the HTTP boundary — the two never share a type, so a column rename cannot silently reshape the API. Per-use-case folders keep everything a slice needs together.

Each feature owns its registration: `Program.cs` calls `AddEmails()`, `AddTemplates()`, `AddDispatch()`, then `MapEmails()`, `MapTemplates()`. Endpoints implement `IEndpoint` (`static abstract Map`), registered explicitly with `group.MapEndpoint<SendEmailEndpoint>()` — no reflection scanning.

Adding a use case: create `Features/<Feature>/<UseCase>/`, drop in the endpoint (plus handler and validator when it earns them), add one `MapEndpoint<T>()` line to the feature file. Nothing else moves.

## API

The API is unauthenticated by design. Authentication and authorization are platform concerns — a gateway, service mesh, or network boundary decides who may call this service, and the service itself only sends email. **It must never be routable from outside that boundary.** See [Deployment](#deployment-notes).

Send requests may carry an `X-Source` header naming the calling system. It is recorded on the email as `source` for filtering and auditing, and is the rate-limit partition key. It is a label, not a credential — nothing is trusted or granted based on it.

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/v1/emails` | Queue an email |
| `GET` | `/v1/emails/{id}` | Read one email and its delivery state |
| `GET` | `/v1/emails?status=&recipient=&template=&source=&limit=&offset=` | List emails |
| `POST` | `/v1/emails/{id}/cancel` | Cancel a still-queued email |
| `GET` | `/v1/templates` | List templates |
| `GET` | `/v1/templates/{key}` | Read a template |
| `PUT` | `/v1/templates/{key}` | Create or replace a template |
| `DELETE` | `/v1/templates/{key}` | Delete a template |
| `POST` | `/v1/templates/{key}/preview` | Render a template against a model without sending |
| `POST` | `/v1/messages` | Accept a message envelope into the inbox |
| `GET` | `/v1/messages/{id}` | Read the processing state of a received message |
| `GET` | `/health` | Liveness plus a Postgres check |

OpenAPI is served at `/openapi/v1.json` in Development.

### Sending

```jsonc
{
  "to": ["someone@example.com"],       // required; cc and bcc also accepted
  "subject": "Hello",                   // required unless a template supplies it
  "html": "<p>Hello</p>",               // html or text (or both) required
  "text": "Hello",
  "from": "billing@example.com",        // falls back to template, then config
  "fromName": "Billing",
  "replyTo": "support@example.com",
  "headers": { "X-Campaign": "welcome" },
  "attachments": [
    { "fileName": "invoice.pdf", "contentType": "application/pdf", "content": "<base64>" }
  ],
  "sendAt": "2026-08-01T09:00:00Z",     // schedule for later
  "maxAttempts": 5,
  "idempotencyKey": "order-42"
}
```

Attachments carrying a `contentId` are embedded as inline resources, referenced from the HTML as `cid:<contentId>`.

Reusing an `idempotencyKey` returns `200` with the original email instead of queueing a second one — safe to retry a failed HTTP call without double-sending.

### Templates

```bash
curl -X PUT localhost:5294/v1/templates/welcome \
  -H 'Content-Type: application/json' \
  -d '{"subject":"Welcome {{ name }}","html":"<p>Hi {{ name }}</p>"}'

curl -X POST localhost:5294/v1/emails \
  -H 'Content-Type: application/json' -H 'X-Source: billing' \
  -d '{"to":["someone@example.com"],"template":"welcome","model":{"name":"Ada"}}'
```

Templates are [Scriban](https://github.com/scriban/scriban). A malformed template is rejected at `PUT` time with `422`, so a broken edit cannot reach a send. `POST /v1/templates/{key}/preview` renders against a model without queueing anything.

## Inbox

Callers that need to send email as part of their own transaction should not call `POST /v1/emails` directly — a crash between their commit and the call loses the email, and a retry after a timeout sends it twice. They write to a transactional outbox instead and deliver here, which is what `/v1/messages` is for.

```
billing txn:      invoice row + outbox row     one commit, both or neither
billing job:      claim row, deliver           retries until accepted
POST /v1/messages: store by message id         duplicate? 200, nothing queued
inbox processor:  handle, queue the email      retry with backoff, then dead
email dispatcher: SMTP send                    the existing pipeline
```

The envelope is [MessagingKit](https://github.com/eduvhc/messaging-kit)'s shape:

```bash
curl -X POST localhost:5294/v1/messages \
  -H 'Content-Type: application/json' \
  -d '{
    "id": "019fb353-c9a8-78ae-9d1d-00a86703ef5e",
    "type": "send-email",
    "payload": "{\"to\":[\"someone@example.com\"],\"subject\":\"Hi\",\"html\":\"<p>Hi</p>\"}",
    "createdAt": "2026-07-30T09:00:00Z"
  }'
```

`202` means stored; `200` means the id was already seen and nothing was queued a second time. The payload is the same JSON `POST /v1/emails` accepts, so templates, attachments, and scheduling all work unchanged.

Deduplication is the message id, which is the inbox table's primary key — a redelivery cannot create a second row. When the handler queues the email it also passes that id as the `idempotencyKey`, so even a message processed twice by two racing workers converges on one email.

Emails arriving this way are recorded with `source: inbox`.

## Rate limiting

Every `/v1` route is rate limited with a fixed window, partitioned by `X-Source`. Requests without the header fall back to a per-remote-IP partition. Exceeding the limit returns `429` with a `Retry-After` header and a problem document; `/health` is never limited.

```jsonc
"RateLimit": {
  "Enabled": true,
  "PermitLimit": 120,       // per window, per source
  "WindowSeconds": 60,
  "QueueLimit": 0,          // 0 rejects immediately instead of waiting
  "Sources": {
    "reports": { "PermitLimit": 20 },              // per-source overrides
    "billing": { "PermitLimit": 600, "WindowSeconds": 60 }
  }
}
```

Two caveats worth knowing. The limiter is **per instance** — with N replicas the effective ceiling is N × `PermitLimit`, so size it accordingly or move the limit to the gateway if you need a global one. And because partitions are created per distinct header value, a caller sending random `X-Source` values would grow the partition table; have the gateway set or strip that header rather than letting arbitrary clients choose it.

## Configuration

Bind through appsettings or environment variables (`Section__Key`).

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:Postgres` | localhost:5433 | |
| `Database:MigrateOnStartup` | `false` | `true` in Development |
| `Smtp:Host` / `Smtp:Port` | `localhost:1026` | |
| `Smtp:Security` | `Auto` | `None`, `Auto`, `SslOnConnect`, `StartTls` |
| `Smtp:Username` / `Smtp:Password` | none | Supply via secrets, not appsettings |
| `Smtp:AcceptAllCertificates` | `false` | Disables TLS certificate validation; local development only |
| `EmailDefaults:FromAddress` | `no-reply@example.com` | Used when neither request nor template names a sender |
| `EmailDefaults:MaxAttempts` | `5` | |
| `EmailDefaults:MaxRecipients` | `50` | Across to + cc + bcc |
| `EmailDefaults:MaxAttachmentBytes` | `10485760` | Total decoded size |
| `EmailDefaults:AllowedRecipientDomains` | empty | Empty allows every domain; set it in staging to avoid mailing real users |
| `Dispatcher:Enabled` | `true` | `false` for an API-only replica |
| `Dispatcher:BatchSize` / `Concurrency` | `20` / `4` | |
| `Dispatcher:PollIntervalSeconds` | `5` | Skipped while batches come back full |
| `Dispatcher:LockDurationSeconds` | `120` | Must exceed the worst-case send time |
| `Dispatcher:BaseRetryDelaySeconds` | `30` | Doubles per attempt |
| `Dispatcher:MaxRetryDelaySeconds` | `3600` | |
| `RateLimit:Enabled` | `true` | `false` disables limiting entirely |
| `RateLimit:PermitLimit` / `WindowSeconds` | `120` / `60` | Per source, per instance |
| `RateLimit:QueueLimit` | `0` | Requests queued once the limit is hit; `0` rejects immediately |
| `RateLimit:Sources:<source>:*` | none | Per-source overrides of the three settings above |
| `Inbox:Enabled` | `true` | `false` accepts messages but runs no processor |
| `Inbox:BatchSize` / `Concurrency` | `50` / `4` | |
| `Inbox:MaxAttempts` | `10` | Then the message is marked `Dead` |
| `Inbox:BaseRetryDelaySeconds` / `MaxRetryDelaySeconds` | `10` / `3600` | Doubles per attempt |

One setting exists only for local development and must stay off elsewhere: `Smtp:AcceptAllCertificates=true` disables TLS certificate validation. SMTP passwords belong in a secret store or environment variables, never in appsettings.

## Migrations

```bash
dotnet dotnet-ef migrations add <Name> -p src/EmailService -s src/EmailService -o Persistence/Migrations
dotnet dotnet-ef database update -p src/EmailService -s src/EmailService
```

Production deployments should run `database update` as a release step and leave `Database:MigrateOnStartup=false`.

## Tests

```bash
dotnet test                                      # unit + integration
dotnet test tests/EmailService.Tests             # unit only, no Docker needed
dotnet test tests/EmailService.IntegrationTests  # needs Docker
```

`EmailService.Tests` uses in-memory fakes: validation, template resolution, idempotency, retry backoff. Instant, no infrastructure.

`EmailService.IntegrationTests` runs against real infrastructure using [TestingKit](https://github.com/eduvhc/testing-kit) fixtures — a Postgres container and a Mailpit SMTP container started once per assembly, with Respawn truncating the `email` schema between tests. It covers what fakes cannot: the `SKIP LOCKED` claim query and concurrent claimers, lock expiry and reclaim, retry and dead transitions, the `text[]`/`jsonb` mappings, the unique idempotency index, migrations, API-key auth and the admin policy, and end-to-end delivery asserted against the Mailpit inbox.

TestingKit comes from nuget.org and needs no credentials. MessagingKit is currently published only to GitHub Packages, so restore needs a token for that feed; `nuget.config` reads it from the environment and nothing secret is committed:

```bash
export GITHUB_PACKAGES_USER=<your-github-user>
export GITHUB_PACKAGES_TOKEN=<PAT with read:packages>   # or: $(gh auth token)
dotnet restore
```

Package source mapping pins `MessagingKit.*` to that feed and everything else to nuget.org. Once MessagingKit is on nuget.org the extra source can go away.

## Adding an email provider

Implement `IEmailSender` in `Transport/<Provider>/` and swap the registration in `Transport/TransportExtensions.cs`:

```csharp
public class ResendEmailSender : IEmailSender
{
    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        // ...
        return SendResult.Ok(providerMessageId);
    }
}
```

Return `SendResult.Permanent` for failures retrying cannot fix — the dispatcher sends those straight to `Dead` instead of burning attempts. The queue, dispatcher, and API are untouched.

## Deployment notes

**The service has no authentication of its own, so the deployment must supply it.** Anything that can reach the port can send mail signed by your domain's SPF/DKIM records, and can read every stored email — recipients, subjects, bodies, and template models — through `GET /v1/emails`. Before deploying, make sure one of these is true:

- a service mesh enforces mTLS and an authorization policy naming the services allowed to call this one, or
- an authenticating gateway is the only route in, and the service accepts traffic from it alone, or
- the service is bound to a private network with no ingress and no public load balancer.

"Nothing has exposed it yet" is not one of those. Verify with an unauthenticated request from outside the intended boundary; it should not connect.

- The image runs as a non-root user and listens on `8080`.
- `/health` covers liveness and Postgres reachability; point both probes at it.
- Every replica runs a dispatcher by default. That is safe, but for predictable throughput run a fixed number of dispatcher replicas and set `Dispatcher:Enabled=false` on the rest.
- `Dispatcher:LockDurationSeconds` must exceed the slowest realistic send, or a second worker will reclaim a row still in flight and the recipient gets the mail twice.

## Package notes

`Microsoft.OpenApi` is pinned to `2.11.0`. The 3.x line breaks the ASP.NET Core 10 OpenAPI source generator (`IOpenApiMediaType.Example` became read-only), and the version resolved transitively by default (`2.0.0`) carries advisory GHSA-v5pm-xwqc-g5wc.
