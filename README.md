# MessagingKit

Transactional outbox and inbox for .NET 10, on EF Core and PostgreSQL.

The framework knows nothing about NATS, RabbitMQ, HTTP, or any other transport. It owns durability and the state machine; you implement `IMessageTransport` and plug in whatever moves the bytes.

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
| `MessagingKit.Abstractions` | `MessageEnvelope`, `IMessageTransport`, `IMessageHandler<T>`, serializer, type registry |
| `MessagingKit.Outbox` | `IOutbox.Add`, EF model config, `SKIP LOCKED` dispatcher, retry and dead-lettering |
| `MessagingKit.Inbox` | durable deduplication, background processor, handler dispatch, retry and dead-lettering |

Outbox and inbox are independent — take one, the other, or both.

## Sending

Register the outbox against your own `DbContext`, name your message types, and supply a transport:

```csharp
builder.Services.AddOutbox<AppDbContext>(builder.Configuration)
    .AddMessage<SendEmail>("send-email")
    .UseTransport<NatsTransport>();
```

Add the tables to your model:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddOutbox();   // messaging.outbox
    modelBuilder.AddInbox();    // messaging.inbox
}
```

They land in your `DbContext`, so `dotnet ef migrations add` picks them up in your own migration history — the package ships no migrations of its own.

Then write the message inside the transaction that produced it:

```csharp
db.Invoices.Add(invoice);
outbox.Add(new SendEmail(customer.Email, "Your invoice"));
await db.SaveChangesAsync();    // both rows commit, or neither does
```

`outbox.Add` only stages an entity — your `SaveChangesAsync` commits it. If the transaction rolls back, the message never existed.

A hosted `OutboxDispatcher` claims pending rows with `SELECT ... FOR UPDATE SKIP LOCKED`, hands each to the transport, and marks it `Sent`. Transient failures are rescheduled with exponential backoff; permanent failures and exhausted attempts become `Dead` and stay queryable.

## Implementing a transport

The whole surface:

```csharp
public sealed class NatsTransport(INatsConnection nats) : IMessageTransport
{
    public async Task<TransportResult> SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        try
        {
            await nats.PublishAsync(envelope.Destination ?? envelope.Type, envelope.Payload, cancellationToken: ct);
            return TransportResult.Ok();
        }
        catch (NatsException ex)
        {
            return TransportResult.Transient(ex.Message);
        }
    }
}
```

Return `Permanent` for failures retrying cannot fix — a malformed destination, a rejected payload. Those skip the retry ladder and go straight to `Dead`.

## Receiving

```csharp
builder.Services.AddInbox<AppDbContext>(builder.Configuration)
    .AddHandler<SendEmail, SendEmailHandler>("send-email");
```

Whatever receives from your transport calls the inbox:

```csharp
if (await inbox.TryStoreAsync(envelope, ct))
{
    // stored; the processor will handle it
}
// false means already seen — ack and move on
```

`TryStoreAsync` is the deduplication point: the message id is the primary key, so a redelivery cannot create a second row. Acknowledge the transport immediately; the background `InboxProcessor` resolves `IMessageHandler<T>`, runs it, and marks the row `Processed`. A throwing handler is retried with backoff, then dead-lettered.

Handlers receive the deserialized message plus a `MessageContext` carrying the message id, type, attempt count, and headers.

```csharp
public sealed class SendEmailHandler(IEmailClient client) : IMessageHandler<SendEmail>
{
    public Task HandleAsync(SendEmail message, MessageContext context, CancellationToken ct = default) =>
        client.SendAsync(message.To, message.Subject, ct);
}
```

## Configuration

Both halves bind the same shape, under `Outbox` and `Inbox`:

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | `false` leaves the tables in place but runs no background job |
| `Schema` / `TableName` | `messaging` / `outbox`, `inbox` | Must match what you passed to `AddOutbox()` / `AddInbox()` |
| `BatchSize` / `Concurrency` | `50` / `4` | Rows claimed per tick, and how many run in parallel |
| `PollIntervalSeconds` | `5` | Skipped while batches come back full |
| `LockDurationSeconds` | `120` | Must exceed the slowest realistic delivery |
| `MaxAttempts` | `10` | Then `Dead` |
| `BaseRetryDelaySeconds` / `MaxRetryDelaySeconds` | `10` / `3600` | Doubles per attempt, capped |

## Operational notes

- **Ordering is not guaranteed.** Messages dispatch in parallel; if you need per-key ordering, that is a transport concern.
- **Delivery is at-least-once.** A process can die after the transport accepts a message but before the row is marked `Sent`. That is precisely why the receiver needs an inbox.
- **`LockDurationSeconds` must exceed your slowest send**, or a second worker reclaims a row still in flight and the message goes out twice.
- **Both tables grow forever.** Add a retention job that deletes `Sent` and `Processed` rows past your audit window; keep `Dead` until someone has looked at them.
- Multiple replicas are safe — `SKIP LOCKED` guarantees a row is claimed by exactly one worker.

## Tests

```bash
dotnet test
```

Runs against a real PostgreSQL container using [TestingKit](https://github.com/eduvhc/testing-kit), with a fake in-memory transport — the framework is transport-agnostic, so the tests need no broker.
