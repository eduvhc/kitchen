# MediatingKit

An in-process mediator for .NET 10 whose dispatch is written at compile time. A source generator finds every handler, writes a mediator with one typed field and one type check per request, and replaces your `AddMediator()` call with a registration of it. No reflection, no lookups by type, no allocations on the hot path; a request with no handler, or two, fails the build.

```csharp
public sealed record DeleteFile(string StorageId) : IRequest<Result>;          // the response type, declared once

public sealed class DeleteFileHandler(IObjectStore store) : IRequestHandler<DeleteFile, Result>
{
    public async ValueTask<Result> Handle(DeleteFile request, CancellationToken ct) =>
        await store.DeleteAsync(request.StorageId, ct);
}

builder.Services.AddMediator();                                  // every handler in the project, registered

Result result = await mediator.Send(new DeleteFile(id), ct);     // the response type is inferred
```

## Quick start

```bash
dotnet add package MediatingKit
```

1. Declare a request: `IRequest<TResponse>`, or `IRequest` when there is nothing to return.
2. Write its handler: `IRequestHandler<TRequest, TResponse>` (or `IRequestHandler<TRequest>`). Public or internal, anywhere in the project.
3. Call `builder.Services.AddMediator()` once, and inject `IMediator` (or just `ISender` / `IPublisher`).

That is all. The package carries the source generator and the build props it needs; there is no assembly scanning to configure and nothing to register per handler.

## Packages

| Package | Contents |
| --- | --- |
| `MediatingKit` | The contracts, the runtime the generated mediator builds on, `AddMediator()`, traces and metrics — and, packed inside it, the source generator and its diagnostics. Depends only on `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Diagnostics`. |

## Requests

| Contract | Handler | Call |
| --- | --- | --- |
| `IRequest<TResponse>` | `IRequestHandler<TRequest, TResponse>` returning `ValueTask<TResponse>` | `await mediator.Send(request, ct)` |
| `IRequest` | `IRequestHandler<TRequest>` returning `ValueTask` | `await mediator.Send(request, ct)` |
| `IStreamRequest<TItem>` | `IStreamRequestHandler<TRequest, TItem>` returning `IAsyncEnumerable<TItem>` | `await foreach (var x in mediator.CreateStream(request, ct))` |

Every request has exactly one handler. A handler for a base type (`IRequestHandler<Shape, string>`) also handles requests derived from it, unless they have their own. A handler can implement its interface explicitly.

**ResultingKit** responses need nothing special: `IRequest<Result>` and `IRequest<Result<T>>` are requests like any other, the handler's `return Error.NotFound(...)` and `return value;` convert as usual, and a failed `Result` comes back as a value, not an exception. The mediator does not interpret responses.

## Notifications

```csharp
public sealed record FileDeleted(string Id) : INotification;

public sealed class AuditFileDeleted : INotificationHandler<FileDeleted> { ... }
public sealed class PurgeThumbnails : INotificationHandler<FileDeleted> { ... }
public sealed class IndexEverything : INotificationHandler<INotification> { ... }   // sees every notification

await mediator.Publish(new FileDeleted(id), ct);
```

A notification reaches every handler for its type, then those for its base types and interfaces. Zero handlers is fine. Handlers run **one after another** by default and the first exception stops the rest; `o.NotificationPublishing = NotificationPublishing.Parallel` starts them together, lets all finish, and rethrows one failure as itself or several as an `AggregateException`.

## Pipeline behaviors

Behaviors wrap every send: validation, logging, transactions. Add them in one line, in the order they should run; the first added is outermost.

```csharp
builder.Services.AddMediator(o => o
    .AddBehavior(typeof(LoggingBehavior<,>))      // open generic: every request
    .AddBehavior(typeof(TransactionBehavior<,>))
    .AddBehavior<DeleteFileValidation>());        // closed: only the requests it implements IPipelineBehavior for

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async ValueTask<TResponse> Handle(TRequest request, PipelineNext<TRequest, TResponse> next, CancellationToken ct)
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        return await next(request, ct);
    }
}
```

- **Narrow an open behavior with constraints.** `where TResponse : Result` runs it only for requests answered with a ResultingKit result; the container skips it for the rest.
- **Short-circuit** by returning without calling `next` — a validation behavior returns `Result.Failure(...)`.
- **Requests without a response** reach behaviors as `IRequest<Unit>`, so one open behavior covers both kinds.
- `next` takes the request and token as arguments instead of capturing them, so the mediator chains the behaviors **once per mediator instance** and reuses the chain: a send through behaviors allocates no delegates.
- Behaviors are ordinary services. `AddBehavior` registers them with the mediator's lifetime; one registered directly as `IPipelineBehavior<,>` runs too, in registration order.
- Streams and notifications have no behaviors.

## Registration and lifetimes

```csharp
builder.Services.AddMediator(o =>
{
    o.Lifetime = ServiceLifetime.Scoped;                              // the default
    o.NotificationPublishing = NotificationPublishing.Sequential;     // the default
});
```

The mediator and its handlers share one lifetime, scoped by default so handlers can take a `DbContext`. If no handler needs scoped services, `ServiceLifetime.Singleton` removes the per-scope cost entirely. The mediator resolves each handler and builds its pipeline on first use and keeps them for its own lifetime: per scope when scoped, for the application when singleton, per resolution of the mediator when transient. A handler you register yourself before `AddMediator()` keeps your registration.

**Across projects.** The mediator is generated for the project that calls `AddMediator()`. It dispatches to that project's handlers and to the handlers of every project it references — including **internal** ones: each project with handlers exports a small hidden class that resolves them, and the calling project's mediator calls through it. Only the request, response and notification types have to be public. Call `AddMediator()` once, from the project that composes the application; a second call from the same project does nothing, a call from another project throws, since that project's mediator sees a different set of handlers.

## Diagnostics

Handler wiring is checked at compile time, by the generator, as build errors:

| Rule | Severity | When |
| --- | --- | --- |
| `MED1001` | Error | A request declared in a project that has handlers has none — neither there nor in a referenced project. A contracts-only project is not checked. |
| `MED1002` | Error | A request or stream request has two or more handlers, in this project or across it and its references. Use a notification to fan out. |
| `MED1003` | Error | A type implements more than one of `IRequest`, `IRequest<T>`, `IStreamRequest<T>` — it would have two response types. |
| `MED1004` | Error | A handler cannot be called from generated code: it is generic, or private, protected or file-local. |

```
error MED1001: 'ArchiveFile' has no handler; add a class implementing IRequestHandler<ArchiveFile, bool>
error MED1002: 'DeleteFile' is handled by more than one handler: DeleteFileHandler, OtherDeleteFileHandler; a request has exactly one
```

If a request's handler lives in a project that cannot see it (a module handling another module's contract), set `dotnet_diagnostic.MED1001.severity = none` for the declaring project in `.editorconfig`.

## How it works

The generator runs in every project that references MediatingKit and writes:

- **A mediator**, in each project that calls `AddMediator()`: a `file sealed class` deriving from `MediatorBase`, with one typed field per handler and a `Send` that is a chain of type checks against the request types found at compile time. A match calls a method typed for that request: with the handler resolved, no behavior and nobody listening, it calls the handler directly — devirtualised, and usually inlined, for a sealed handler. A pipeline is only built for a request type some behavior applies to; once one scope finds none, the container remembers it and later scopes skip resolving behaviors altogether. With behaviors, the outermost is called directly with the rest of the chain as `next`. The response is reinterpreted with `Unsafe.BitCast` rather than boxed; that is sound because a request declares exactly one response (`MED1003`), and for request types that could be subclassed the generated check also compares `typeof(TResponse)`.
- **Publish**, per notification type, resolves its handlers once into a generated holder with one typed field each and calls them one after another; a handler that does not finish synchronously hands the rest to an awaited loop. Parallel publishing and telemetry take the general path.
- **An interceptor** for each `AddMediator()` call, which replaces it with a registration of that mediator and its handlers. That is why `AddMediator()` is a single line with no assembly to scan. Interceptors need the `MediatingKit.Generated` namespace in `<InterceptorsNamespaces>`; the package's `build/MediatingKit.props` adds it. Without the generator the call throws, explaining what is missing.
- **Exports**, in each project with handlers whose messages are public, so other projects' mediators can reach them.

To see what was generated, set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and look under `obj/`.

**Costs worth knowing.** Dispatch is a linear chain of type checks, most derived first; each is a pointer comparison, but a project with hundreds of request types pays for the ones before its match. A struct request is boxed by `Send`, as the parameter is an interface. The first send of each request type on each mediator instance resolves its handler from the container, so with the default scoped lifetime each scope pays for a mediator and its handlers (see *Performance*); a singleton mediator pays once.

## Performance

BenchmarkDotNet, .NET 10, each library called through its `IMediator` interface, resolved once. Handlers return cached responses, so the numbers are the mediators' own cost. MediatingKit runs scoped (its default, resolved from one scope), Mediator singleton, MediatR with its defaults (transient handlers).

BENCHMARK_TABLE

Run them with:

```bash
dotnet run -c Release --project kits/mediating-kit/benchmarks/MediatingKit.Benchmarks -- --filter '*'
```

## Observability

MediatingKit traces and measures every send and publish on its own, using only `ActivitySource` and `Meter` from the runtime, with no OpenTelemetry package. With nobody listening the cost is one check per call; nothing is recorded until the host listens:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(MediatingKitInstrumentation.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(MediatingKitInstrumentation.MeterName));
```

**Request and notification contents are never recorded**, only the message type's name. Exceptions are recorded by type, not message. A handler that returns a failed `Result` completed its dispatch, so it counts as a success here; the failure belongs to the caller.

**Span** — `send {Request}` or `publish {Notification}` (`send DeleteFile`), kind `Internal`, around the behaviors and handler or around every notification handler. Streams are not traced.

| Attribute | Value |
| --- | --- |
| `mediatingkit.operation` | `send` or `publish` |
| `mediatingkit.message.type` | The message type's full name: `Files.DeleteFile` |
| `mediatingkit.outcome` | `success` or `failure` |
| `error.type` | The exception's full type name, on failure |

**Metrics** — on the `MediatingKit` meter, created through the host's `IMeterFactory`:

| Instrument | Type | Unit | Attributes |
| --- | --- | --- | --- |
| `mediatingkit.dispatches` | Counter | `{dispatch}` | `mediatingkit.operation`, `mediatingkit.message.type`, `mediatingkit.outcome`, plus `error.type` on failure |
| `mediatingkit.dispatch.duration` | Histogram | `s` | Same as above |

The histogram advises buckets from 100 µs to 10 s.

## Later: transports

The generator already holds the complete list of request types for a project, which is what a transport needs to turn a message type name back into a type and call `mediator.Send(request)` without generic arguments. A generated `TryGetRequestType(string name)` is the natural next step; the envelope and JSON layer is out of scope for now.
