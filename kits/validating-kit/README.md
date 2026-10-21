# ValidatingKit

Small, fluent validation that returns a [ResultingKit](../resulting-kit) `Result`. Rules read like the requirement, every invalid field is reported at once under its JSON path, and a failure is an ordinary `Error` that maps to `ValidationProblemDetails` over HTTP.

```bash
dotnet add package ValidatingKit
```

No dependencies beyond ResultingKit. No reflection or expression trees at validation time, and a valid value costs one small allocation.

## Before and after

A hand-written validator collects messages and builds the error itself:

```csharp
public static Error? Validate(DeleteFileRequested? request)
{
    var errors = new List<string>();
    if (request is null) errors.Add("request is required.");
    else if (string.IsNullOrWhiteSpace(request.StorageId)) errors.Add("storageId is required.");
    return errors.Count > 0 ? Error.Validation("INVALID_REQUEST", string.Join(" ", errors)) : null;
}
```

With ValidatingKit it states the rules and nothing else:

```csharp
using ValidatingKit;

public static Result Validate(DeleteFileRequested? request) =>
    Validation.That(request, "INVALID_REQUEST")
        .Required()
        .Field(r => r.StorageId, f => f.NotBlank())
        .ToResult();
```

A blank `StorageId` fails with:

| | |
| --- | --- |
| `Error.Code` | `INVALID_REQUEST` |
| `Error.Kind` | `Validation` |
| `Error.Message` | `storageId: Must not be blank.` |
| `Error.FieldErrors` | `{ "storageId": ["Must not be blank."] }` |

A `null` request fails with `request: Is required.` under the path `""`, the value itself. It fails even without `.Required()`; the rule is there to make it explicit or to choose the message.

The entry point is `Validation.That` rather than `Validate.That` so that it still compiles inside a method called `Validate`, where the method name would hide a type of the same name.

## Rules

Every rule takes an optional `message` that replaces its default. Rules other than `Required`, `NotBlank` and `NotEmpty` pass `null`, so an optional field is validated only when it is present.

| Values | Rules |
| --- | --- |
| Anything | `Required()`, `Equal(x)`, `NotEqual(x)`, `Must(predicate, message)` |
| `string` | `NotBlank()`, `NotEmpty()`, `MinLength(n)`, `MaxLength(n)`, `Length(n)`, `Length(min, max)`, `Matches(regex)`, `Matches("pattern")`, `Email()` |
| Collections | `NotEmpty()`, `MinLength(n)`, `MaxLength(n)`, `Length(n)`, `Length(min, max)`, `Each(rules)` |
| Numbers, dates, any `IComparable<T>` | `GreaterThan(x)`, `GreaterThanOrEqualTo(x)`, `LessThan(x)`, `LessThanOrEqualTo(x)`, `Between(min, max)` (inclusive) |
| `Guid` | `NotEmpty()` |
| Enums | `IsInEnum()` |
| Objects | `Valid(validator)`, `Valid(v => v.Field(...))` |

Comparison, `Guid` and enum rules also work on the nullable forms (`int?`, `DateOnly?`, `Guid?`). String lengths count UTF-16 characters, as `string.Length` does. `Email()` is deliberately loose, like `EmailAddressAttribute`: one `@` with text on both sides and no white space. `Matches("pattern")` uses .NET's regex cache with a one-second timeout; pass a `[GeneratedRegex]` for the cheapest match. A match that times out counts as invalid. `IsInEnum()` accepts named values only, so combined `[Flags]` values fail.

For anything else, `Must`:

```csharp
.Field(o => o.Sku, f => f.NotBlank().Must(sku => sku.StartsWith("SKU-", StringComparison.Ordinal), "Must start with SKU-."))
```

## Failures

- **Every field is checked.** One result reports every invalid field, so a client can fix them all in one round trip.
- **Each field stops at its first failure.** `f.NotBlank().MaxLength(64)` never reports both for one value.
- **Paths are camelCase**, matching System.Text.Json's web defaults: `r => r.StorageId` is `storageId`, `o => o.Address.City` is `address.city`, a collection element is `lines[2]`, and a member of one is `lines[2].sku`.
- **`Error.Message` lists every failure** as `path: message`, in the order the rules ran, so a contract that carries only `{ code, message }`, such as an event, still says everything: `name: Must not be blank. lines[1].quantity: Must be greater than 0.`
- **`Error.FieldErrors` groups messages by path**, in the same order.

The name comes from the selector's source text, captured by the compiler through `CallerArgumentExpression`, and is cached per selector. A selector that is not a plain member access, such as `o => o.Lines.Count(l => l.Gift)`, cannot be named and throws on first use; pass the name instead:

```csharp
.Field("giftCount", o => o.Lines.Count(l => l.Gift), f => f.LessThanOrEqualTo(3))
```

## Composing

### Conditions

`When` applies rules only if a condition on the whole value holds:

```csharp
Validation.That(order, "order.invalid")
    .When(o => o.Ship, v => v.Field(o => o.Address, f => f.Required()))
```

### Cross-field rules

Give `Field` a two-argument function to read the rest of the value. The failure is reported against the field:

```csharp
.Field(o => o.End, (f, o) => f.GreaterThanOrEqualTo(o.Start, "Must not be before the start."))
.Field(o => o.Phone, (f, o) => o.ContactByPhone ? f.NotBlank() : f)
```

`Must` on the validation itself checks the whole value and reports at the root.

### Nested objects

`Valid` validates a member's own members, with paths under the member's:

```csharp
.Field(o => o.Address, f => f.Required().Valid(a => a
    .Field(x => x.Street, s => s.NotBlank())
    .Field(x => x.PostalCode, p => p.NotBlank().MaxLength(10))))
```

A blank street is `address.street`. Without `Required()`, a `null` address is simply not validated.

### Collections

`Each` applies element rules to every element, under its index:

```csharp
.Field(o => o.Tags, f => f.MaxLength(10).Each(t => t.NotBlank().MaxLength(32)))
.Field(o => o.Lines, f => f.NotEmpty().Each(l => l.Required().Valid(lineValidator)))
```

`Each` works on arrays, `List<T>`, and the `IList<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `IReadOnlyCollection<T>` and `IEnumerable<T>` interfaces. Collections nest: `rows[1][3]`.

## Reusable validators

Derive from `Validator<T>` to name a set of rules once and use it anywhere:

```csharp
public sealed class LineValidator() : Validator<Line>("line.invalid")
{
    public override Validation<Line> Apply(Validation<Line> line) => line
        .Field(l => l.Sku, f => f.NotBlank())
        .Field(l => l.Quantity, f => f.GreaterThan(0));
}
```

- `validator.Validate(line)` returns a `Result<Line>` with the validator's code. A `null` line fails as `line: Is required.`
- `f.Valid(validator)` and `l => l.Valid(validator)` apply it to a nested member or to each element. The outer validation's code wins.
- `Validation.That(x, code).Include(validator)` adds its rules to another validation.

A validator keeps no state, so one instance can be shared. ValidatingKit has no registration extension, because that would add a dependency on `Microsoft.Extensions.DependencyInjection.Abstractions` for one line the host can write itself:

```csharp
builder.Services.AddSingleton<IValidator<Line>, LineValidator>();
```

## Results

`ToResult()` returns a `Result`: the shared `Result.Success()`, or a failure with an `ErrorKind.Validation` error. `ToValueResult()` returns a `Result<T>` carrying the value, so validation chains straight into the next step:

```csharp
public Task<Result<Receipt>> HandleAsync(PlaceOrder? command, CancellationToken ct) =>
    Validation.That(command, "order.invalid")
        .Field(c => c.CustomerId, f => f.NotEmpty())
        .Field(c => c.Lines, f => f.NotEmpty().Each(l => l.Valid(lineValidator)))
        .ToValueResult()
        .BindAsync(c => orders.PlaceAsync(c, ct));
```

With `ResultingKit.AspNetCore`, a validation failure becomes a `ValidationProblemDetails`, with the field errors under `errors` and `Error.Message` as `detail`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "customerId: Must not be empty. lines[0].quantity: Must be greater than 0.",
  "errors": {
    "customerId": ["Must not be empty."],
    "lines[0].quantity": ["Must be greater than 0."]
  },
  "code": "order.invalid",
  "traceId": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
}
```

## Cost

Validation runs on every request or message, so the hot path stays lean:

- A `Validation<T>` and each `Field<T>` are structs. A valid value allocates one 40-byte collector, however many fields, nested objects and elements it checks, and `ToResult()` returns the shared success. A unit test pins this.
- Rule lambdas that capture nothing are cached by the compiler. Capturing a local, such as `f => f.GreaterThan(min)`, allocates a closure per call; prefer the two-argument `Field` overload to read sibling values.
- Field names are derived once per selector and cached. Paths for nested members and elements are built only when something fails.
- Default messages are formatted only on failure.

## Not included

- **Async rules.** A rule that needs I/O, such as "this email is not taken", belongs in the handler, returning `Error.Conflict(...)` with `FieldErrors` if the client should see it against a field. Keeping rules synchronous keeps validation allocation-free and lets it run anywhere.
- **Localisation.** Default messages are English. Pass `message` to any rule for your own text.
- **A cascade switch.** Fields always run and each stops at its first failure; there is no mode that stops the whole validation at the first failure.

## Tests

`ValidatingKit.UnitTests` covers every rule, paths, nesting, collections, conditions, message joining and the `Result` integration. The kit has no infrastructure, so there is no integration suite.
