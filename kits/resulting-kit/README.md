# ResultingKit

Explicit success and failure for operations that can fail in an expected way. Exceptions stay for bugs and broken wiring; a `Result` is for failures the caller is meant to handle.

```bash
dotnet add package ResultingKit
```

No dependencies. MessagingKit and MailingKit use it for their transport contracts, so a transport you write for either returns the same type. To turn results into HTTP responses, add `ResultingKit.AspNetCore` — see [Mapping to HTTP](#mapping-to-http).

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

`ResultingKit` itself has no ASP.NET Core dependency. The HTTP mapping ships separately:

```bash
dotnet add package ResultingKit.AspNetCore
```

A success becomes `204 No Content` for a `Result` and `200 OK` with the value for a `Result<T>`. A failure becomes an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem, written as `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Order 42 does not exist.",
  "code": "order.not_found"
}
```

`detail` is `Error.Message` and `code` is `Error.Code`. The status comes from `Error.Kind`:

| Kind | Status |
| --- | --- |
| `Validation` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `Transient` | 503 |
| `Failure` | 500 |

### Minimal APIs

`ToHttpResult` returns an `IResult`; `ToHttpResultAsync` does the same for a `Task<Result>` or `Task<Result<T>>`:

```csharp
using ResultingKit.AspNetCore;

app.MapGet("/orders/{id:guid}", (Guid id, OrderService orders, CancellationToken ct) =>
    orders.GetAsync(id, ct).ToHttpResultAsync());

app.MapDelete("/orders/{id:guid}", (Guid id, OrderService orders, CancellationToken ct) =>
    orders.CancelAsync(id, ct).ToHttpResultAsync());
```

Register `builder.Services.AddProblemDetails()` and failures go through ASP.NET Core's `IProblemDetailsService`, which adds a `traceId` and runs any `CustomizeProblemDetails` you configured.

### Controllers

`ToActionResult` returns an `IActionResult`; `ToActionResultAsync` awaits first. A failure is an `ObjectResult` carrying the `ProblemDetails`:

```csharp
using ResultingKit.AspNetCore;

[HttpGet("{id:guid}")]
public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
    orders.GetAsync(id, ct).ToActionResultAsync();
```

### Choosing the success response

Pass a function to build the success response yourself. It is not called on failure:

```csharp
app.MapPost("/orders", (NewOrder input, OrderService orders) =>
    orders.Create(input).ToHttpResult(order => TypedResults.Created($"/orders/{order.Id}", order)));

[HttpPost]
public IActionResult Post(NewOrder input) =>
    orders.Create(input).ToActionResult(order => CreatedAtAction(nameof(Get), new { id = order.Id }, order));
```

A plain `Result` takes a parameterless function, such as `() => TypedResults.Accepted(location)`.

### Choosing the status

Every method takes an optional `statusCode` function, `Func<Error, int>`. `ToStatusCode()` is the default mapping, so an override only has to name its exceptions:

```csharp
static int Status(Error error) => error.Code switch
{
    "order.name_required" => StatusCodes.Status422UnprocessableEntity,
    _ => error.ToStatusCode(),
};

app.MapPost("/orders", (NewOrder input, OrderService orders) =>
    orders.Create(input).ToHttpResult(order => TypedResults.Created($"/orders/{order.Id}", order), Status));

app.MapGet("/orders/{id:guid}", (Guid id, OrderService orders, CancellationToken ct) =>
    orders.GetAsync(id, ct).ToHttpResultAsync(statusCode: Status));
```

The override is a plain argument rather than a global setting, so each endpoint says what it does and a test can call it directly. `title` and `type` follow whatever status it picks.

`error.ToProblemDetails(statusCode)` builds the same `ProblemDetails` on its own, for an exception handler or a custom response.
