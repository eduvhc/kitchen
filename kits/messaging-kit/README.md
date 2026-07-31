# MessagingKit

Transactional outbox and inbox for .NET 10, on EF Core and PostgreSQL.

Reference it from a module and that module can send and receive durably. Messages between modules in one host travel over the same outbox → transport → inbox seam they would use across a network, so moving a module into its own deployment later changes one registration and no module code.

The framework knows nothing about brokers, HTTP, or any other transport. It owns durability and the state machine; you implement `IMessageTransport` and plug in whatever moves the bytes. An in-process transport ships in the box.

## The problem

You cannot atomically write to your database and talk to another system:

```csharp
db.Invoices.Add(invoice);
await db.SaveChangesAsync();     // committed
await broker.PublishAsync(msg);  // process dies here — invoice exists, nobody was told
```

Swap the order and you get the opposite bug: the message goes out, the transaction rolls back, and you've announced an invoice that does not exist.

**The outbox** closes that gap by making the intent to send part of the same transaction. **The inbox** closes the matching gap on the receiving side, where a retried delivery would otherwise be processed twice.

Outbox means *don't lose it*. Inbox means *don't do it twice*. A broker replaces neither — it only carries the message in between.

## Packages

| Package | Contents |
| --- | --- |
| `MessagingKit` | `AddMessaging()` — outbox, inbox, and in-process delivery in one registration |
| `MessagingKit.Abstractions` | `MessageEnvelope`, `IMessageTransport`, `IMessageHandler<T>`, serializer, type registry |
| `MessagingKit.Outbox` | `IOutbox.Add`, EF model config, `SKIP LOCKED` dispatcher, retry and dead-lettering |
| `MessagingKit.Inbox` | durable deduplication, background processor, handler dispatch, retry and dead-lettering |
| `MessagingKit.InProcess` | `InProcessTransport` — delivers outbox rows straight into the inbox |
| `MessagingKit.Testing` | `DrainMessagingAsync()` — run both halves to completion inside a test |

Take `MessagingKit` for the whole thing, or the halves on their own if you only send or only receive.

## Modules

A module registers what it handles. Nothing else in the host needs to know:

A module never names the host's `DbContext` — it only declares what it sends and handles, so it depends on `MessagingKit.Abstractions` alone:

```csharp
// inside the email module
public static IServiceCollection AddEmailModule(this IServiceCollection services) =>
    services.AddMessageHandler<SendEmail, SendEmailHandler>();
```

```csharp
// inside the billing module — sends, does not handle
services.AddMessageContract<SendEmail>();
```

The host owns the context, declares it once, and wires the modules:

```csharp
builder.Services.AddMessaging<AppDbContext>(builder.Configuration);

builder.Services.AddEmailModule();
builder.Services.AddBillingModule();
```

Order does not matter — both sides only add to the service collection.

For a single-module host the fluent form does the same thing in one statement:

```csharp
builder.Services.AddMessaging<AppDbContext>(builder.Configuration)
    .Handles<SendEmail, SendEmailHandler>();
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddMessaging();   // messaging.outbox + messaging.inbox
}
```

The tables land in your `DbContext`, so `dotnet ef migrations add` picks them up in your own migration history — the package ships no migrations of its own.

`AddMessaging` is safe to call once per module. The dispatcher and processor are registered once no matter how many modules call it, and in-process delivery is the default transport.

Billing then sends inside its own transaction, and the email module handles it:

```csharp
db.Invoices.Add(invoice);
outbox.Add(new SendEmail(customer.Email, "Your invoice"));
await db.SaveChangesAsync();    // both rows commit, or neither does
```

## Message names

A message carries a name on the wire. It is derived from the type — `SendEmail` becomes `send-email`, `HTTPRequest` becomes `http-request` — so `Handles<TMessage, THandler>()` registers the same name for the sender and the handler and they cannot drift apart.

Renaming the class renames the message, which orphans rows already queued. Pin the name on anything in production:

```csharp
[Message("email.send.v2")]
public record SendEmail(string To, string Subject);
```

The string overloads are still there when you want to be explicit: `AddMessage<T>("name")`, `AddHandler<T, THandler>("name")`.

## Transports and routing

In-process delivery is the default. Route specific types elsewhere as modules move out of the host:

```csharp
services.AddMessaging<AppDbContext>(config)
    .Handles<SendEmail, SendEmailHandler>()          // stays local
    .UseTransportFor<BrokerTransport, InvoiceIssued>();  // this one goes to the broker
```

Or set a different default and keep only some types local:

```csharp
.UseInProcessTransport("send-email")   // named types stay in the host
.UseTransport<BrokerTransport>()       // everything else goes to the broker
```

A routing key matches either a message's **destination** or its **type**, so a module can address another module directly and override the type's usual route:

```csharp
outbox.Add(new SendEmail(...), destination: "email-module");
```

```csharp
.UseTransport<BrokerTransport>("email-module")   // everything addressed there
.UseInProcessTransport("send-email")             // everything of this type
```

Resolution runs destination first, then message type, then the default, then any `IMessageTransport` registered directly in DI. A message with no route at all is dead-lettered immediately rather than retried — a missing registration is a wiring bug, and retrying it for ten attempts only buries the cause.

Implementing a transport is one method:

```csharp
public sealed class BrokerTransport(IBrokerClient broker) : IMessageTransport
{
    public async Task<TransportResult> SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        try
        {
            await broker.PublishAsync(envelope.Destination ?? envelope.Type, envelope.Payload, ct);
            return TransportResult.Ok();
        }
        catch (BrokerException ex)
        {
            return TransportResult.Transient(ex.Message);
        }
    }
}
```

Return `Permanent` for failures retrying cannot fix — a malformed destination, a rejected payload. Those skip the retry ladder and go straight to `Dead`.

## Receiving

Handlers take the deserialized message plus a `MessageContext` carrying the message id, type, attempt count, and headers:

```csharp
public sealed class SendEmailHandler(IEmailClient client) : IMessageHandler<SendEmail>
{
    public Task HandleAsync(SendEmail message, MessageContext context, CancellationToken ct = default) =>
        client.SendAsync(message.To, message.Subject, ct);
}
```

With `InProcessTransport` the outbox hands messages to the inbox for you. When something else receives them — a broker subscriber, an HTTP endpoint — call the inbox yourself:

```csharp
if (await inbox.TryStoreAsync(envelope, ct))
{
    // stored; the processor will handle it
}
// false means already seen — ack and move on
```

`TryStoreAsync` is the deduplication point: the message id is the primary key, so a redelivery cannot create a second row. Acknowledge the transport immediately; the background `InboxProcessor` resolves `IMessageHandler<T>`, runs it, and marks the row `Processed`. A throwing handler is retried with backoff, then dead-lettered.

## À la carte

Outbox and inbox work on their own if you do not want both:

```csharp
builder.Services.AddOutbox<AppDbContext>(builder.Configuration)
    .AddMessage<SendEmail>()
    .UseTransport<BrokerTransport>();

builder.Services.AddInbox<AppDbContext>(builder.Configuration)
    .AddHandler<SendEmail, SendEmailHandler>();
```

```csharp
modelBuilder.AddOutbox();   // messaging.outbox
modelBuilder.AddInbox();    // messaging.inbox
```

## Tracing

Trace context is captured when a message is staged and carried in its headers, so the send and the handling that follows it belong to one trace rather than two unrelated ones. Subscribe to the source:

```csharp
builder.Services.AddOpenTelemetry().WithTracing(t => t
    .AddSource(MessagingDiagnostics.ActivitySourceName)
    .AddAspNetCoreInstrumentation());
```

You get a `send {type}` producer span at dispatch and a `handle {type}` consumer span at processing, both parented to whatever was active when the caller wrote the row — with the message id, type, and attempt number as tags, and failures marked as errors. Without this, an async boundary makes debugging strictly worse than a method call.

## Compile-time checks

`MessagingKit.Abstractions` ships an analyzer, so referencing it is all it takes:

| Rule | Severity | Catches |
| --- | --- | --- |
| `MK1001` | Error | Two message types resolving to the same wire name — the receiver cannot tell them apart |
| `MK1002` | Warning | A handler nothing registers, so it never runs |
| `MK1003` | Error | `[Message("")]`, which throws when the attribute is read at startup |
| `MK1004` | Off by default | A message with no `[Message]`, where renaming the type renames it on the wire |

`MK1002` is a warning rather than an error because the registration may legitimately live in another project; suppress it there. Turn on `MK1004` once messages are in production and a class rename would orphan queued rows:

```ini
dotnet_diagnostic.MK1004.severity = warning
```

## Startup validation

A message delivered in-process with no handler registered fails the host at boot:

```
MessagingKit is misconfigured:
  - 'send-email' is delivered in-process but no IMessageHandler<SendEmail> is registered.
    Register a handler with Handles<SendEmail, THandler>(), or route it to another transport.
```

That is a failed deploy instead of a dead-lettered row discovered days later. Messages routed to another transport are skipped — they are handled wherever they land.

## Configuration

Both halves bind the same shape, under `Outbox` and `Inbox`:

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | `false` leaves the tables in place but runs no background job |
| `Schema` / `TableName` | `messaging` / `outbox`, `inbox` | Must match what you passed to `AddMessaging()` / `AddOutbox()` / `AddInbox()` |
| `BatchSize` / `Concurrency` | `50` / `4` | Rows claimed per tick, and how many run in parallel |
| `PollIntervalSeconds` | `5` | Skipped while batches come back full |
| `LockDurationSeconds` | `120` | Must exceed the slowest realistic delivery |
| `MaxAttempts` | `10` | Then `Dead` |
| `BaseRetryDelaySeconds` / `MaxRetryDelaySeconds` | `10` / `3600` | Doubles per attempt, capped |

## Operational notes

- **Delivery is prompt, not instant.** Committing a transaction that staged outbox rows wakes the dispatcher, and storing to the inbox wakes the processor, so a message does not wait out the poll interval. The interval is the fallback — a missed signal costs latency, never correctness.
- **Ordering is not guaranteed.** Messages dispatch in parallel; if you need per-key ordering, that is a transport concern.
- **Delivery is at-least-once.** A process can die after the transport accepts a message but before the row is marked `Sent`. That is precisely why the receiver needs an inbox.
- **`LockDurationSeconds` must exceed your slowest send**, or a second worker reclaims a row still in flight and the message goes out twice.
- **Both tables grow forever.** Add a retention job that deletes `Sent` and `Processed` rows past your audit window; keep `Dead` until someone has looked at them.
- **One inbox table per host**, shared by every module and routed by message type. A module gets its own inbox when it gets its own database.
- Multiple replicas are safe — `SKIP LOCKED` guarantees a row is claimed by exactly one worker.

## Testing against it

`MessagingKit.Testing` drives both halves to completion so a test asserts on the effect of a message rather than sleeping until the background loops happen to run:

```csharp
outbox.Add(new SendEmail("ada@example.com", "Your invoice"));
await db.SaveChangesAsync();

await services.DrainMessagingAsync();

Assert.AreEqual(1, mailbox.Sent.Count);
```

It drives the same dispatcher and processor production uses, so what the test exercises is what ships. `DrainOutboxAsync()` and `DrainInboxAsync()` run one half when you want to assert on what was delivered before anything handles it.

## Tests

```bash
dotnet test                                                     # every kit
dotnet test kits/messaging-kit/tests/MessagingKit.UnitTests     # no Docker needed
dotnet test kits/messaging-kit/tests/MessagingKit.IntegrationTests   # needs Docker
```

`MessagingKit.UnitTests` covers naming, registration, signalling, startup validation, and the analyzer — no infrastructure, so it runs in about a second and parallelises.

`MessagingKit.IntegrationTests` runs against a real PostgreSQL container using [TestingKit](../testing-kit), with a fake in-memory transport — the framework is transport-agnostic, so the tests need no broker. It is `[assembly: DoNotParallelize]`: the suite shares one database and resets between tests.

Test doubles shared by both live in `MessagingKit.TestSupport` so they cannot drift apart.
