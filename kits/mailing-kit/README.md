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
| Not sending it twice | MessagingKit inbox |
| Retry, backoff, dead-lettering | MessagingKit inbox |
| Scheduling (`sendAt`) | MessagingKit outbox |
| Templates, validation, SMTP | MailingKit |
| A record of what was sent | MailingKit send log |

## Packages

| Package | Contents |
| --- | --- |
| `MailingKit` | `SendEmail`, the handler, templates, send log |
| `MailingKit.Smtp` | `SmtpEmailSender` on MailKit |

```bash
dotnet add package MessagingKit
dotnet add package MailingKit
dotnet add package MailingKit.Smtp
```

`MailingKit.Smtp` is separate so a host on Resend or SES never pulls in MailKit.

## Quick start

Everything below is complete — copy it, point the connection string at your database, and it runs.

### 1. Your `DbContext`

Both kits map their tables into the context you already own, so `dotnet ef migrations add` picks them up in your own migration history. Neither package ships migrations.

```csharp
using MailingKit.Persistence;
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
using MailingKit;
using MessagingKit.Outbox.Abstractions;

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

**No idempotency key, no `maxAttempts`, no `sendAt` on this type** — they would duplicate MessagingKit. The message id deduplicates, the inbox owns the retry ladder, and scheduling is `outbox.Add(message, sendAt: whenever)`.

Attachments travel inside the message payload, so keep them small; anything large belongs in object storage with a link.

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

## Another provider

Implement one method:

```csharp
using MailingKit.Transport;

public sealed class ResendEmailSender(HttpClient http) : IEmailSender
{
    public async Task<SendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/emails", Map(email), ct);

        if (response.IsSuccessStatusCode)
        {
            return SendResult.Ok(await ReadProviderIdAsync(response, ct));
        }

        return (int)response.StatusCode >= 500
            ? SendResult.Transient($"{(int)response.StatusCode} from provider")
            : SendResult.Permanent(await response.Content.ReadAsStringAsync(ct));
    }
}
```

```csharp
builder.Services.AddScoped<IEmailSender, ResendEmailSender>();   // instead of AddSmtpTransport()
```

`OutgoingEmail` arrives fully resolved — templates rendered, recipients validated — so a transport only has to move bytes. Return `Permanent` for what retrying cannot fix; the inbox dead-letters those instead of burning ten attempts on a malformed address.

## The send log

`email.email_log` records one row per message: recipients, subject, template, outcome, the provider's message id, and the error if there was one. It is **not** a queue — no status machine, no locking, no `Queued` state. Anything still in flight lives in `messaging.inbox`.

```sql
select to_addresses, subject, status, provider_message_id, last_error
from email.email_log
where created_at > now() - interval '1 day'
order by created_at desc;
```

Keyed unique on `message_id`, so a redelivered message updates its row rather than adding a second one.

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

SMTP settings live on `AddSmtpTransport`: `Host`, `Port`, `Security`, `Username`, `Password`, `TimeoutSeconds`, `AcceptAllCertificates`.

**`AcceptAllCertificates` disables TLS certificate validation and is for local development only.** SMTP passwords belong in a secret store or environment variables, never in `appsettings.json`.

## Testing against it

`MessagingKit.Testing` drives both halves to completion, so a test asserts on the email rather than sleeping:

```csharp
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
- **Bodies are not stored**, only metadata. If you need to reproduce exactly what a customer received, keep the template versioned.
- **There is no suppression list.** Nothing tracks hard bounces, so a dead address is retried on every send. Add one before sending at volume — it is how a sending domain gets blocklisted.
- **Set `AllowedRecipientDomains` outside production.** It is the cheapest guard against a staging run mailing real customers.
