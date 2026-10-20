# ResultingKit

Explicit success and failure for operations that can fail in an expected way. Exceptions stay for bugs and broken wiring; a `Result` is for failures the caller is meant to handle.

```bash
dotnet add package ResultingKit
```

No dependencies. MessagingKit and MailingKit use it for their transport contracts, so a transport you write for either returns the same type.

## The types

| Type | What it is |
| --- | --- |
| `Result` | Success, or failure with an `Error`. |
| `Result<T>` | Success with a `T`, or failure with an `Error`. Derives from `Result`. |
| `Error` | `Code`, `Message`, `Kind`. A record, so `with` works. |
| `ErrorKind` | `Failure`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `Transient`. |

`Code` is stable and machine-readable (`smtp.mailbox_unavailable`), meant for logs, metrics, and `switch`. `Message` is for people and may change.

## Returning one

Values and errors convert implicitly, so most methods never name `Result` in a `return`:

```csharp
using ResultingKit;

public async Task<Result<Order>> GetAsync(Guid id, CancellationToken ct)
{
    var order = await db.Orders.FindAsync([id], ct);

    if (order is null)
    {
        return Error.NotFound("order.not_found", $"Order {id} does not exist.");
    }

    return order;
}
```

For a non-generic outcome, `return Result.Success();` or return an `Error`.

## Reading one

`IsSuccess` and `IsFailure` carry nullability annotations, so the compiler knows `Error` is set after a failure check:

```csharp
var result = await orders.GetAsync(id, ct);

if (result.IsFailure)
{
    logger.LogWarning("Lookup failed: {Code}", result.Error.Code);
    return;
}

Ship(result.Value);
```

`Value` throws on a failure, with the error in the message. `TryGetValue` and `GetValueOrDefault` avoid that.

## Chaining

`Map` transforms a value, `Bind` chains another fallible step, `Match` folds both branches into one, and `MapError` rewrites the error. Each short-circuits on the first failure:

```csharp
var label = await orders.GetAsync(id, ct)
    .BindAsync(order => shipping.QuoteAsync(order, ct))
    .MatchAsync(
        quote => $"{quote.Carrier}: {quote.Price:C}",
        error => error.Kind == ErrorKind.NotFound ? "No such order" : "Unavailable");
```

## Retrying

`Error.IsTransient` is true only for `ErrorKind.Transient`. Every other kind is permanent — retrying a validation failure or a 5xx refusal cannot help. That is the whole contract MessagingKit's outbox and MailingKit's inbox handler rely on: a transient error goes back on the retry ladder, anything else is dead-lettered straight away.

## Mapping to HTTP

The kit ships no ASP.NET Core dependency. A host maps kinds itself, typically:

| Kind | Status |
| --- | --- |
| `Validation` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `Transient` | 503 |
| `Failure` | 500 |
