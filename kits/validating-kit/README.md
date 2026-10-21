# ValidatingKit

Fluent validation that returns a [ResultingKit](../resulting-kit) `Result`. Rules read like the requirement, every invalid field is reported at once under its JSON path, and a failure is an ordinary `Error` that maps to `ValidationProblemDetails` over HTTP. It runs about 7× faster than FluentValidation, and a valid value costs a single 40-byte allocation — see [Performance](#performance).

```bash
dotnet add package ValidatingKit
dotnet add package ValidatingKit.AspNetCore   # optional: validate minimal API and controller arguments automatically
```

No dependencies beyond ResultingKit. No reflection, no expression trees, no registration: one `using ValidatingKit;` and you are writing rules.

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
        .Field(r => r.StorageId, f => f.NotBlank());
```

A validation converts itself to a `Result` or `Result<T>` wherever one is expected, so there is nothing to finish. `ToResult()` and `ToValueResult()` do the same explicitly, for when you keep chaining. The code is optional too: leave it out and failures carry `validation.failed`.

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
    public override Validation<Line> Apply(Validation<Line> validation) => validation
        .Field(l => l.Sku, f => f.NotBlank())
        .Field(l => l.Quantity, f => f.GreaterThan(0));
}
```

- `validator.Validate(line)` returns a `Result<Line>` with the validator's code. A `null` line fails as `line: Is required.`
- `f.Valid(validator)` and `l => l.Valid(validator)` apply it to a nested member or to each element. The outer validation's code wins.
- `Validation.That(x, code).Include(validator)` adds its rules to another validation.

A validator keeps no state, so one instance can be shared. In an ASP.NET Core app, `ValidatingKit.AspNetCore` registers them for you (see [ASP.NET Core](#aspnet-core)). Anywhere else it is one line, which is why the core package has no dependency on dependency injection:

```csharp
services.AddSingleton<IValidator<Line>, LineValidator>();
```

## Results

Return a validation from a method typed `Result` or `Result<T>` and it converts itself. Otherwise `ToResult()` returns a `Result`: the shared `Result.Success()`, or a failure with an `ErrorKind.Validation` error. `ToValueResult()` returns a `Result<T>` carrying the value, so validation chains straight into the next step:

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

## ASP.NET Core

```bash
dotnet add package ValidatingKit.AspNetCore
```

Register your validators, then switch validation on for a group, an endpoint, or every controller. Invalid input never reaches your handler:

```csharp
using ValidatingKit.AspNetCore;

builder.Services.AddProblemDetails();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var api = app.MapGroup("/api").WithValidation();

api.MapPost("/orders", (PlaceOrder order, OrderService orders) =>
    orders.Place(order).ToHttpResult(o => TypedResults.Created($"/api/orders/{o.Id}", o)));
```

That's all. Every argument whose type has a registered `IValidator<T>`, here `PlaceOrder`, is validated before the handler runs. A failure comes back as the validation problem shown above, built by ResultingKit.AspNetCore through ASP.NET Core's own problem details services, so it carries `code` and `traceId` and runs your `CustomizeProblemDetails`.

| You want | Write |
| --- | --- |
| Register one validator | `services.AddValidator<PlaceOrderValidator>()` |
| Register every validator in an assembly | `services.AddValidatorsFromAssemblyContaining<Program>()` |
| Validate a whole group of endpoints | `app.MapGroup("/api").WithValidation()` |
| Validate one endpoint | `app.MapPost(...).WithValidation()` |
| Validate inline, without a class | `app.MapPost(...).Validate<Note>(n => n.Field(x => x.Text, f => f.NotBlank()))` |
| Return 422 instead of 400 | `.WithValidation(_ => 422)`, or `.Validate<T>(rules, statusCode: _ => 422)` |
| Validate controller actions | `services.AddControllers().WithValidation()` |

Details that keep it out of your way:

- **Decided once, at startup.** Which arguments of which endpoint get validated is worked out when the endpoint is built. An endpoint with nothing to validate gets no filter at all; a validated one costs a validator lookup and the validation itself.
- **Validators come from the request's services**, so a validator can take scoped dependencies. Registration helpers add singletons, and they skip any validator interface you have already registered yourself, so your registration wins.
- **OpenAPI knows.** Every endpoint that validates advertises a `400` `HttpValidationProblemDetails` response, so generated clients and docs show the validation errors without a `.ProducesValidationProblem()` on each endpoint.
- **Groups and endpoints combine.** `WithValidation()` on a group and again on one of its endpoints validates once, with the endpoint's status. `Validate<T>()` adds inline rules on top of any registered validator.
- **Mistakes fail at startup.** `Validate<Note>()` on an endpoint without a `Note` parameter throws when the endpoints are built, naming the endpoint, rather than silently validating nothing.
- **Native AOT and trimming.** `WithValidation()` closes generic types over parameter types at startup and is annotated `[RequiresDynamicCode]`; assembly scanning is `[RequiresUnreferencedCode]`. In an AOT app use `AddValidator<T>()` and `Validate<T>()`, which need neither.

The names avoid .NET 10's own `AddValidation()` (Microsoft.Extensions.Validation, which validates DataAnnotations attributes), so both can live in one app.

## Performance

Validation runs on every request and every message, so it is built to cost next to nothing. Measured with [BenchmarkDotNet](benchmarks/ValidatingKit.Benchmarks) against FluentValidation 12.2 with the same rules and the same stop-at-first-failure-per-field behaviour, on .NET 10:

| Case | ValidatingKit | FluentValidation | Faster | ValidatingKit allocates | FluentValidation allocates |
| --- | ---: | ---: | ---: | ---: | ---: |
| Simple, valid | 7.6 ns | 57.3 ns | 7.5× | 40 B | 632 B |
| Simple, invalid | 64.3 ns | 416.1 ns | 6.5× | 472 B | 1,816 B |
| Order, valid | 186.7 ns | 1,459.7 ns | 7.8× | 40 B | 6,032 B |
| Order, invalid | 667.5 ns | 4,639.7 ns | 7.0× | 2,656 B | 17,080 B |

BenchmarkDotNet 0.15.8, .NET 10.0.13, AMD Ryzen 9 9950X3D. Absolute numbers move with the machine; the ratios are what to look at. For scale, the hand-written validator from the top of this page takes 2.2 ns valid and 17.5 ns invalid, so on a valid value ValidatingKit costs about 5 ns more than writing the `if` yourself.

*Simple* is the `DeleteFileRequested` example above. *Order* checks 7 fields, a nested address, and 5 order lines through a reusable `Validator<Line>` (19 fields in all); the invalid order fails 9 of them.

Where the speed comes from:

- **No allocation while validating.** `Validation<T>` and `Field<T>` are readonly structs that share one 40-byte failure collector, however many fields, nested objects and elements are checked. A valid value returns the shared `Result.Success()`. A unit test pins the 40 bytes.
- **Names without reflection.** The compiler hands over each selector's source text (`CallerArgumentExpression`); it is parsed once, and later calls find it by the reference of the interned literal in a lock-free slot table — no hashing, no expression trees.
- **Nothing is built until something fails.** Paths for nested members and elements, default messages and the joined `Message` are produced only on failure, then formatted without boxing into exactly-sized strings and an array-backed field map.
- **Tight loops.** Arrays and `List<T>` are walked as spans, `Email()` is vectorised, and generic null checks are written so the JIT removes them for value types.

Two habits keep it that way in your code: write rule lambdas that capture nothing (read sibling values with the two-argument `Field` overload rather than closing over a local), and prefer a `[GeneratedRegex]` to `Matches("pattern")`.

Run the benchmarks yourself:

```bash
dotnet run -c Release --project kits/validating-kit/benchmarks/ValidatingKit.Benchmarks -- --filter '*'
```

## Coming from FluentValidation

| FluentValidation | ValidatingKit |
| --- | --- |
| `class X : AbstractValidator<T>` with `RuleFor` in the constructor | `class X : Validator<T>` overriding `Apply`, or `Validation.That(value)` inline |
| `RuleFor(x => x.Name).NotEmpty()` | `.Field(x => x.Name, f => f.NotBlank())` |
| `NotNull()` | `Required()` |
| `NotEmpty()` on a collection or `Guid` | `NotEmpty()` |
| `MaximumLength(n)`, `MinimumLength(n)`, `Length(min, max)` | `MaxLength(n)`, `MinLength(n)`, `Length(min, max)` |
| `InclusiveBetween(a, b)` | `Between(a, b)` |
| `EmailAddress()`, `Matches(...)`, `IsInEnum()` | `Email()`, `Matches(...)`, `IsInEnum()` |
| `.WithMessage("...")` | the rule's `message` argument: `NotBlank("Name it.")` |
| `.When(x => ...)` | `.When(x => ..., v => v.Field(...))`, or `(f, x) => x.Flag ? f.NotBlank() : f` |
| `SetValidator(new AddressValidator())` | `Valid(addressValidator)` |
| `RuleForEach(x => x.Lines)` | `.Field(x => x.Lines, f => f.Each(l => ...))` |
| `ValidationResult.Errors` | `Error.FieldErrors`, already shaped for `ValidationProblemDetails` |
| `MustAsync`, `ValidateAsync` | not offered; do I/O in the handler (see below) |

## Not included

- **Async rules.** A rule that needs I/O, such as "this email is not taken", belongs in the handler, returning `Error.Conflict(...)` with `FieldErrors` if the client should see it against a field. Keeping rules synchronous keeps validation allocation-free and lets it run anywhere.
- **Localisation.** Default messages are English. Pass `message` to any rule for your own text.
- **A cascade switch.** Fields always run and each stops at its first failure; there is no mode that stops the whole validation at the first failure.

## Tests

`ValidatingKit.UnitTests` covers every rule, paths, nesting, collections, conditions, message joining, the `Result` integration, the allocation budget and the examples in this README. The core package has no infrastructure, so it has no integration suite. `ValidatingKit.AspNetCore.UnitTests` covers registration and argument discovery, and `ValidatingKit.AspNetCore.IntegrationTests` runs real minimal API and controller endpoints on an in-memory `TestServer`, with no Docker needed. `benchmarks/ValidatingKit.Benchmarks` holds the comparison above.
