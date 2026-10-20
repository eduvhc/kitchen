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

A success becomes `204 No Content` for a `Result` and `200 OK` with the value for a `Result<T>`. A failure becomes an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem, built by ASP.NET Core's own problem details services rather than by hand, so it looks like every other problem your app returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Order 42 does not exist.",
  "code": "order.not_found",
  "traceId": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
}
```

`detail` is `Error.Message` and `code` is `Error.Code`. `title` and `type` are ASP.NET Core's defaults for the status (`ProblemDetailsDefaults` for minimal APIs, `ApiBehaviorOptions.ClientErrorMapping` for controllers, which starts from the same table). The status comes from `Error.Kind`:

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

A failure is what `TypedResults.Problem` returns. Register `builder.Services.AddProblemDetails()` and it is written through `IProblemDetailsService`, which adds the `traceId` and runs any `CustomizeProblemDetails` you configured. Without it, ASP.NET Core writes the problem as plain JSON: same fields, no `traceId`, no customisation.

### Controllers

`ToActionResult` returns an `IActionResult`; `ToActionResultAsync` awaits first. A failure is an `ErrorActionResult`, which does what `ControllerBase.Problem()` does: when it executes it asks MVC's `ProblemDetailsFactory` for the problem, adds `code`, and writes it through an `ObjectResult`, so content negotiation and your formatters apply. The default factory always adds the `traceId`, takes `title` and `type` from `ClientErrorMapping`, and runs `AddProblemDetails(o => o.CustomizeProblemDetails = ...)`. As with `Problem()`, that callback runs before `code` is added.

```csharp
using ResultingKit.AspNetCore;

[HttpGet("{id:guid}")]
public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
    orders.GetAsync(id, ct).ToActionResultAsync();
```

In a controller unit test, `ErrorActionResult` exposes `Error` and `StatusCode`, so there is nothing to execute:

```csharp
var problem = (ErrorActionResult)controller.Get(id);
Assert.AreEqual(404, problem.StatusCode);
Assert.AreEqual("order.not_found", problem.Error.Code);
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

The override is a plain argument rather than a global setting, so each endpoint says what it does and a test can call it directly. `title` and `type` follow whatever status it picks; a status ASP.NET Core has no default for gets the reason phrase as `title` on minimal APIs and no `title` on controllers, exactly as `TypedResults.Problem` and `ControllerBase.Problem` behave.
