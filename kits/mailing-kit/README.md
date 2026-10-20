# MailingKit

Email as a module for .NET 10, on EF Core and PostgreSQL.

A product references it and gains the ability to send email durably. There is no queue, no dispatcher, and no service to deploy — [MessagingKit](../messaging-kit) owns durability, retries, and dead-lettering, and MailingKit is the handler on the other end of it.

```csharp
db.Invoices.Add(invoice);
outbox.Add(new SendEmail { To = [customer.Email], Template = "invoice" });
await db.SaveChangesAsync();    // invoice and email commit together, or neither does
```

The sending module knows nothing about SMTP, templates, or retries. It writes a row in its own transaction and moves on.

## Why there is no queue here

An earlier version of this was a service with its own `emails` table, `SKIP LOCKED` dispatcher, and retry ladder. All of it duplicated what MessagingKit's outbox already does, field for field, so it was deleted.

What is left is the part that is actually about email.

| Concern | Owner |
| --- | --- |
| Not losing the message | MessagingKit outbox |
| Not sending it twice | MessagingKit inbox, plus an idempotency key per send — see [Retries and duplicates](#retries-and-duplicates) |
| Retry, backoff, dead-lettering | MessagingKit inbox |
| Scheduling (`sendAt`) | MessagingKit outbox |
| Templates, validation, SMTP | MailingKit |
| A record of what was sent | MailingKit send log |

## Packages

| Package | Contents |
| --- | --- |
| `MailingKit` | `SendEmail`, the handler, templates, send log, traces and metrics |
| `MailingKit.Smtp` | `SmtpEmailSender` on MailKit |
| `MailingKit.Resend` | `ResendEmailSender` on `HttpClient` and the [Resend](https://resend.com) API — no SDK |

```bash
dotnet add package MessagingKit
dotnet add package MailingKit
dotnet add package MailingKit.Smtp      # or MailingKit.Resend
```

Each transport is its own package, so a host on Resend never pulls in MailKit and a host on SMTP never pulls in `Microsoft.Extensions.Http`. Register exactly one.

`MailingKit` is organised by feature, and each feature is one namespace:

| Namespace | Contents |
| --- | --- |
| `MailingKit` | `AddMailing`, `MailingOptions`, `MailingKitInstrumentation` |
| `MailingKit.Sending` | `SendEmail`, `IEmailSender`, `OutgoingEmail`, `SendReceipt`, `Attachment`, `EmailDefaultsOptions`, the exceptions |
| `MailingKit.Templates` | `ITemplateStore`, `IWritableTemplateStore`, `ITemplateRenderer`, `EmailTemplate`, `TemplateOptions`, `modelBuilder.AddEmailTemplates()` |
| `MailingKit.SendLog` | `EmailLog`, `EmailStatus`, `modelBuilder.AddMailing()` |

The transports keep their own namespaces, `MailingKit.Smtp` and `MailingKit.Resend`.

## Quick start

Everything below is complete — copy it, point the connection string at your database, and it runs.

### 1. Your `DbContext`

Both kits map their tables into the context you already own, so `dotnet ef migrations add` picks them up in your own migration history. Neither package ships migrations.

```csharp
using MailingKit.SendLog;
using MailingKit.Templates;
using MessagingKit;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessaging();        // messaging.outbox, messaging.inbox
        modelBuilder.AddMailing();          // email.email_log
        modelBuilder.AddEmailTemplates();   // email.templates — only with database templates
    }
}
```

### 2. `Program.cs`

```csharp
using MailingKit;
using MailingKit.Smtp;
using MessagingKit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Outbox, inbox, and in-process delivery between modules in this host.
builder.Services.AddMessaging<AppDbContext>(builder.Configuration);

// The send-email handler, templates, and the send log.
builder.Services.AddMailing<AppDbContext>(o =>
{
    o.Templates.UseFiles("EmailTemplates");
    o.Defaults.FromAddress = "no-reply@example.com";
    o.Defaults.FromName = "Example";
});

builder.Services.AddSmtpTransport(o =>
{
    o.Host = builder.Configuration["Smtp:Host"]!;
    o.Port = builder.Configuration.GetValue("Smtp:Port", 587);
    o.Username = builder.Configuration["Smtp:Username"];
    o.Password = builder.Configuration["Smtp:Password"];
});

var app = builder.Build();
app.Run();
```

`AddMailing` registers the handler through `AddMessageHandler<SendEmail, …>`, so MessagingKit's startup validation covers it: a host that wires the outbox but forgets `AddMailing` fails at boot rather than dead-lettering a message at 3am.

### 3. Create the tables

```bash
dotnet ef migrations add AddMessagingAndMailing
dotnet ef database update
```

### 4. Send

Inject `IOutbox` wherever the email is a consequence of work you are already doing:

```csharp
using MailingKit.Sending;
using MessagingKit.Outbox;

public sealed class InvoiceService(AppDbContext db, IOutbox outbox)
{
    public async Task IssueAsync(Invoice invoice, string customerEmail, CancellationToken ct)
    {
        db.Invoices.Add(invoice);

        outbox.Add(new SendEmail
        {
            To = [customerEmail],
            Template = "invoice",
            Model = new Dictionary<string, object?>
            {
                ["reference"] = invoice.Reference,
                ["total"] = invoice.Total,
            },
            Source = "billing",
        });

        await db.SaveChangesAsync(ct);
    }
}
```

`outbox.Add` stages a row; your `SaveChangesAsync` commits it. If the transaction rolls back, the email never existed. Committing wakes the dispatcher, so delivery does not wait out a poll interval.

## The message

```csharp
new SendEmail
{
    To = ["someone@example.com"],       // required; Cc and Bcc also accepted
    Subject = "Hello",                   // required unless a template supplies it
    Html = "<p>Hello</p>",               // Html or Text (or both) required
    Text = "Hello",
    From = "billing@example.com",        // falls back to the template, then to Defaults
    FromName = "Billing",
    ReplyTo = "support@example.com",
    Template = "welcome",                // renders Subject/Html/Text when set
    Model = new() { ["name"] = "Ada" },
    Headers = new() { ["X-Campaign"] = "welcome" },
    Attachments = [new Attachment
    {
        FileName = "invoice.pdf",
        ContentType = "application/pdf",
        Content = Convert.ToBase64String(bytes),
    }],
    Source = "billing",                  // free-text label on the send log
}
```

Anything explicitly set wins over the template, which wins over `Defaults`.

**No idempotency key, no `maxAttempts`, no `sendAt` on this type** — they would duplicate MessagingKit. The message id deduplicates — and becomes the provider's [idempotency key](#retries-and-duplicates) — the inbox owns the retry ladder, and scheduling is `outbox.Add(message, sendAt: whenever)`.

Attachments travel inside the message payload, so keep them small; anything large belongs in object storage with a link.

## Retries and duplicates

The inbox makes sure a message is *handled* once, but a send can still be *attempted* more than once: if the provider accepts the email and its answer is lost to a timeout, the inbox sees a transient failure and retries. Without help from the provider, that retry delivers the email a second time.

So the handler gives every send an `OutgoingEmail.IdempotencyKey` derived from the inbox message id — `mailingkit-` followed by the id's 32 hex digits. Every retry of the same message carries the same key, and two messages never share one, even with identical content. What a transport does with it:

| Transport | Uses the key as | Effect on a retry after a lost response |
| --- | --- | --- |
| `MailingKit.Resend` | The `Idempotency-Key` request header | Resend returns the original email instead of sending another, for 24 hours |
| `MailingKit.Smtp` | The `Message-ID`, as `<key>@<sender domain>` | Delivered again, but with the same `Message-ID`, so mail systems that deduplicate on it drop the copy |

SMTP has no idempotency handshake, so the Message-ID is the best it can do; it also ties a delivered email back to its row in `messaging.inbox`. A `Message-ID` set through `Headers` wins. Sending through Resend's SMTP relay? Give the message a `Resend-Idempotency-Key` header of your own for the stronger guarantee.

`IdempotencyKey` is null on an `OutgoingEmail` you build by hand, and a transport then sends without one.

**A retry sends exactly what the first attempt sent.** Before its first send, the handler saves the rendered subject, sender, Reply-To and bodies on the message's send-log row, and every retry sends from that row instead of rendering again. A template edited, deactivated or deleted between attempts, or a changed `Defaults` value, cannot change the payload — which matters because Resend rejects a reused key with a different payload (`409 invalid_idempotent_request`), and a dead letter there would mean an email that was probably delivered and is logged as failed. The bodies are cleared once the email is sent.

A message whose row already says `Sent` is not sent again, which covers the inbox running a handler a second time after failing to record its success.

## Templates

Both stores are [Scriban](https://github.com/scriban/scriban). Pick one at registration.

**Files** — versioned with your code, reviewed in pull requests, no admin surface:

```csharp
o.Templates.UseFiles("EmailTemplates");
```

```
EmailTemplates/
  welcome.subject.scriban     required
  welcome.html.scriban        optional
  welcome.text.scriban        optional
```

**Database** — editable at runtime by people who are not engineers:

```csharp
o.Templates.UseDatabase();
```

Then call `modelBuilder.AddEmailTemplates()` and inject `IWritableTemplateStore` to build your own editing screen. Skip that call with file templates, or you inherit a table nothing reads.

Omit both and a message naming a template fails with an explanatory error rather than sending something blank.

## Resend

`MailingKit.Resend` sends through Resend's HTTP API (`POST /emails`) with a typed `HttpClient` from `IHttpClientFactory`. There is no Resend SDK in the dependency graph.

```bash
dotnet add package MailingKit.Resend
```

```csharp
using MailingKit.Resend;

builder.Services.AddResendTransport(builder.Configuration);   // instead of AddSmtpTransport()
```

It binds the `Resend` section:

```json
{
  "Resend": {
    "BaseUrl": "https://api.resend.com/",
    "TimeoutSeconds": 30
  }
}
```

| Setting | Default | Notes |
| --- | --- | --- |
| `ApiKey` | — | **Required.** Put it in a secret store or `Resend__ApiKey`, never in `appsettings.json` |
| `BaseUrl` | `https://api.resend.com/` | Change only for a proxy or a stub |
| `TimeoutSeconds` | `30` | Per request; a timeout is reported as transient |

Options are validated at startup, so a host with no `ApiKey` fails to boot instead of dead-lettering every email. `AddResendTransport(o => o.ApiKey = ...)` sets them in code instead.

Everything on `OutgoingEmail` is sent: sender name, To/Cc/Bcc, Reply-To, HTML and text bodies, custom headers, and attachments. An attachment with a `ContentId` goes out as an inline image, referenced from the HTML as `cid:`.

How Resend's answers reach the inbox:

| Response | `ErrorKind` | Inbox |
| --- | --- | --- |
| `2xx` | — | `SendReceipt` with Resend's email id, stored on the send log |
| `429`, `5xx`, timeout, network failure | `Transient` | Retried |
| `422` | `Validation` | Dead-lettered |
| `401` / `403` | `Unauthorized` / `Forbidden` | Dead-lettered |
| `409` | `Conflict` (`Transient` for `concurrent_idempotent_requests`) | Dead-lettered (retried) |
| any other `4xx` | `Failure` | Dead-lettered |

Error codes are `resend.` plus Resend's own error name — `resend.validation_error`, `resend.rate_limit_exceeded` — and the message is Resend's, prefixed with the status. A body that is not Resend's JSON (a proxy's HTML page, say) is kept as the message under `resend.http_<status>`.

**Retries are idempotent.** Each request carries `OutgoingEmail.IdempotencyKey` as Resend's `Idempotency-Key` header, so a retry after a lost response returns the email Resend already accepted rather than sending a second one. Resend honours a key for 24 hours — far longer than the inbox's retry ladder — and accepts keys of up to 256 characters; MailingKit's are 43. Retries resend the content [pinned on the first attempt](#retries-and-duplicates), so the payload matches and Resend's `409 invalid_idempotent_request` — same key, different payload — cannot come from a template edit.

## Writing another transport

Implement one method:

```csharp
using MailingKit.Sending;
using ResultingKit;

public sealed class AcmeMailSender(HttpClient http) : IEmailSender
{
    public async Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("send", Map(email), ct);

        if (response.IsSuccessStatusCode)
        {
            return new SendReceipt(await ReadProviderIdAsync(response, ct));
        }

        return (int)response.StatusCode is 429 or >= 500
            ? Error.Transient("acme.unavailable", $"{(int)response.StatusCode} from provider")
            : Error.Failure("acme.rejected", await response.Content.ReadAsStringAsync(ct));
    }
}
```

```csharp
builder.Services.AddHttpClient<IEmailSender, AcmeMailSender>();   // instead of AddSmtpTransport()
```

`OutgoingEmail` arrives fully resolved — templates rendered, recipients validated — so a transport only has to move bytes. Return an `Error.Transient` only for what retrying can fix; any other [ResultingKit](../resulting-kit) error kind is permanent, and the inbox dead-letters those instead of burning ten attempts on a malformed address. Let the caller's cancellation throw rather than turning it into an error. If the provider supports idempotent sends, pass `email.IdempotencyKey` to it — it is what keeps a retry after a lost response from delivering twice. `MailingKit.Resend` is a complete, small example to copy from.

## The send log

`email.email_log` records one row per message: recipients, subject, template, outcome, the provider's message id, and the error if there was one. Until the email is sent, it also holds the rendered bodies, so a retry can send them again unchanged. It is **not** a queue — no status machine, no locking, no `Queued` state. Anything still in flight lives in `messaging.inbox`.

```sql
select to_addresses, subject, status, provider_message_id, last_error
from email.email_log
where created_at > now() - interval '1 day'
order by created_at desc;
```

Keyed unique on `message_id`, so a redelivered message updates its row rather than adding a second one. The row is written just before the first send, as `Failed` with `last_error` saying the attempt was interrupted; the outcome overwrites that, so a row still showing it means the process died while the provider was answering.

## Observability

MailingKit traces and measures every send on its own, the way `HttpClient` and EF Core do: it uses only `ActivitySource` and `Meter` from the runtime, with no OpenTelemetry package. Nothing is recorded until the host listens:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(MailingKitInstrumentation.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(MailingKitInstrumentation.MeterName));
```

`AddMailing` wraps whichever `IEmailSender` is registered (SMTP, Resend, or your own), whether the transport is added before or after it, so a custom transport is instrumented without doing anything.

An email is a message, so each send also appears inside MessagingKit's `process send-email` span when MessagingKit's tracing is on. That span covers the whole handler — templates, the send log, the send; MailingKit's span covers only the call to the provider.

OpenTelemetry has no semantic conventions for email, so the attributes use the `mailingkit.` prefix, apart from the general `error.type`.

**Span** — `send email`, kind `Client`, one per call to the transport:

| Attribute | Value |
| --- | --- |
| `mailingkit.provider` | `smtp`, `resend`, or the lowercased sender type name without its `EmailSender` / `MailSender` / `Sender` suffix |
| `mailingkit.email.recipient_count` | To + Cc + Bcc |
| `mailingkit.email.attachment_count` | Number of attachments |
| `mailingkit.outcome` | `sent` or `failed` |
| `mailingkit.email.provider_message_id` | The provider's id, on success |
| `error.type` | The `Error.Code` (`resend.rate_limit_exceeded`), or the exception's full type name |
| `mailingkit.error.kind` | The `ErrorKind`, in snake case: `transient`, `validation`, `not_found`, … — `transient` is the only one the inbox retries |

A failure sets the span status to `Error` with the error code as its description; the provider's message is left out because it can echo recipient addresses. An exception is recorded as an `exception` event and rethrown unchanged, cancellation included. No addresses, subjects, or bodies are recorded anywhere.

**Metrics** — on the `MailingKit` meter, created through the host's `IMeterFactory`:

| Instrument | Type | Unit | Attributes |
| --- | --- | --- | --- |
| `mailingkit.client.sent.emails` | Counter | `{email}` | `mailingkit.provider`, `mailingkit.outcome`, plus `error.type` and `mailingkit.error.kind` on failure |
| `mailingkit.client.send.duration` | Histogram | `s` | Same as above |

Both count attempts, so a send the inbox retries three times counts three. The histogram advises OpenTelemetry's bucket boundaries for seconds (5 ms to 10 s). Provider message ids are on the span only, never on metrics.

## Configuration

`AddMailing` takes a lambda rather than a configuration section, so it is all in one place and typo-proof:

| Setting | Default | Notes |
| --- | --- | --- |
| `Schema` | `email` | Holds the send log and, with database templates, the templates table |
| `Defaults.FromAddress` | `no-reply@localhost` | Used when neither message nor template names a sender |
| `Defaults.FromName` / `Defaults.ReplyTo` | none | |
| `Defaults.MaxRecipients` | `50` | Across To + Cc + Bcc |
| `Defaults.MaxAttachmentBytes` | `10485760` | Total decoded size |
| `Defaults.AllowedRecipientDomains` | empty | Empty allows every domain; set it in staging so tests cannot mail real customers |
| `Templates.UseFiles(dir, ext)` | `EmailTemplates`, `scriban` | |
| `Templates.UseDatabase()` | — | |

SMTP settings live on `AddSmtpTransport`: `Host`, `Port`, `Security`, `Username`, `Password`, `TimeoutSeconds`, `AcceptAllCertificates`. Resend settings live in the `Resend` section — see [Resend](#resend).

**`AcceptAllCertificates` disables TLS certificate validation and is for local development only.** SMTP passwords belong in a secret store or environment variables, never in `appsettings.json`.

## Testing against it

`MessagingKit.Testing` drives both halves to completion, so a test asserts on the email rather than sleeping:

```csharp
using MailingKit.Sending;
using MailingKit.SendLog;
using MessagingKit.Testing;

outbox.Add(new SendEmail { To = ["ada@example.com"], Subject = "Hi", Text = "Hi" });
await db.SaveChangesAsync();

await services.DrainMessagingAsync();

var log = await db.Set<EmailLog>().SingleAsync();
Assert.AreEqual(EmailStatus.Sent, log.Status);
```

```bash
dotnet test                                                 # every kit
dotnet test kits/mailing-kit/tests/MailingKit.UnitTests     # no Docker needed
dotnet test kits/mailing-kit/tests/MailingKit.IntegrationTests   # needs Docker
```

Integration tests run against real Postgres and a real SMTP server via [TestingKit](../testing-kit), covering the whole path: staged in a transaction, carried by MessagingKit, handled here, delivered to a mailbox. Nothing in them is faked.

They are `[assembly: DoNotParallelize]` — they share one database and reset between tests, so running them concurrently makes them trample each other.

## Operational notes

- **Delivery is prompt, not instant.** Committing wakes the outbox dispatcher and storing to the inbox wakes the processor, so a message does not sit out the poll interval. The interval is the fallback.
- **A failed send throws.** `EmailSendException` carries `IsPermanent`, and the inbox decides whether to retry or dead-letter. Nothing here retries.
- **The send log grows forever.** Add a retention job that deletes old `Sent` rows past your audit window, and keep `Failed` until someone has looked at them.
- **Bodies are not kept once sent.** They stay on the row only until the send succeeds, and on `Failed` rows until you delete them. If you need to reproduce exactly what a customer received, keep the template versioned.
- **There is no suppression list.** Nothing tracks hard bounces, so a dead address is retried on every send. Add one before sending at volume — it is how a sending domain gets blocklisted.
- **Set `AllowedRecipientDomains` outside production.** It is the cheapest guard against a staging run mailing real customers.
