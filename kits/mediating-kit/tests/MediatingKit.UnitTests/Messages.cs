using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MediatingKit.UnitTests;

// The requests, notifications, handlers and behaviors this project's generated mediator dispatches to. The generator
// runs over this project like any other, so these double as an end-to-end check of what it writes.

/// <summary>What the handlers and behaviors did, per container, so parallel tests stay apart.</summary>
public sealed class Journal
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => [.. _entries];

    public void Write(string entry) => _entries.Enqueue(entry);
}

// ResultingKit responses, as the README shows them.

public sealed record DeleteFile(string StorageId) : IRequest<Result>;

public sealed class DeleteFileHandler(Journal journal) : IRequestHandler<DeleteFile, Result>
{
    public ValueTask<Result> Handle(DeleteFile request, CancellationToken ct)
    {
        journal.Write($"delete {request.StorageId}");

        return ValueTask.FromResult(request.StorageId == "missing"
            ? Result.Failure(Error.NotFound("file.not_found", "No such file."))
            : Result.Success());
    }
}

public sealed record StoredFile(string Id, long Size);

public sealed record GetFile(string Id) : IRequest<Result<StoredFile>>;

public sealed class GetFileHandler : IRequestHandler<GetFile, Result<StoredFile>>
{
    public async ValueTask<Result<StoredFile>> Handle(GetFile request, CancellationToken ct)
    {
        await Task.Yield();

        if (request.Id == "missing")
        {
            return Error.NotFound("file.not_found", "No such file.");
        }

        return new StoredFile(request.Id, 42);
    }
}

// A struct request with a value-type response, which the allocation test sends.

public readonly record struct Sum(int Left, int Right) : IRequest<int>;

public sealed class SumHandler : IRequestHandler<Sum, int>
{
    public ValueTask<int> Handle(Sum request, CancellationToken ct) => new(request.Left + request.Right);
}

public sealed record Ping(int Value) : IRequest<int>;

public sealed class PingHandler : IRequestHandler<Ping, int>
{
    public ValueTask<int> Handle(Ping request, CancellationToken ct) => new(request.Value + 1);
}

// A request with no response.

public sealed record Touch(string Id) : IRequest;

public sealed class TouchHandler(Journal journal) : IRequestHandler<Touch>
{
    public ValueTask Handle(Touch request, CancellationToken ct)
    {
        journal.Write($"touch {request.Id}");
        return ValueTask.CompletedTask;
    }
}

public sealed record Nudge : IRequest;

public sealed class NudgeHandler : IRequestHandler<Nudge>
{
    public ValueTask Handle(Nudge request, CancellationToken ct) => ValueTask.CompletedTask;
}

// A handler that implements the interface explicitly, which the generated code must still reach.

public sealed record Echo(string Text) : IRequest<string>;

public sealed class EchoHandler : IRequestHandler<Echo, string>
{
    ValueTask<string> IRequestHandler<Echo, string>.Handle(Echo request, CancellationToken ct) => new(request.Text);
}

// Cancellation and failures.

public sealed record Wait(TimeSpan For) : IRequest<string>;

public sealed class WaitHandler : IRequestHandler<Wait, string>
{
    public async ValueTask<string> Handle(Wait request, CancellationToken ct)
    {
        await Task.Delay(request.For, ct);
        return "waited";
    }
}

public sealed record Explode(bool Asynchronously) : IRequest<int>;

public sealed class ExplodeHandler : IRequestHandler<Explode, int>
{
    public async ValueTask<int> Handle(Explode request, CancellationToken ct)
    {
        if (request.Asynchronously)
        {
            await Task.Yield();
        }

        throw new InvalidOperationException("boom");
    }
}

// A handler for a base request type, reached for a derived request too.

public record Shape(string Name) : IRequest<string>;

public sealed record Circle(double Radius) : Shape("circle");

public sealed class ShapeHandler : IRequestHandler<Shape, string>
{
    public ValueTask<string> Handle(Shape request, CancellationToken ct) => new($"shape {request.Name}");
}

// Lifetimes: each handler instance reports its own identity.

public sealed record WhoHandles : IRequest<Guid>;

public sealed class WhoHandlesHandler : IRequestHandler<WhoHandles, Guid>
{
    private readonly Guid _id = Guid.NewGuid();

    public ValueTask<Guid> Handle(WhoHandles request, CancellationToken ct) => new(_id);
}

// A generic request: skipped by MED1001 because it is open, and so the one way to send something nothing handles.

public sealed record Unhandled<T> : IRequest<T>;

// Streams.

public sealed record CountTo(int Limit) : IStreamRequest<int>;

public sealed class CountToHandler : IStreamRequestHandler<CountTo, int>
{
    public async IAsyncEnumerable<int> Handle(CountTo request, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= request.Limit; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

// Notifications: two handlers for the type itself, one for an interface it implements, one for every notification.

public interface IFileEvent : INotification
{
    string Id { get; }
}

public sealed record FileDeleted(string Id) : IFileEvent;

public sealed record FileRenamed(string Id, string To) : IFileEvent;

public sealed record NobodyListens : INotification;

public sealed class AuditFileDeleted(Journal journal) : INotificationHandler<FileDeleted>
{
    public ValueTask Handle(FileDeleted notification, CancellationToken ct)
    {
        journal.Write($"audit {notification.Id}");
        return ValueTask.CompletedTask;
    }
}

public sealed class PurgeFileDeleted(Journal journal) : INotificationHandler<FileDeleted>
{
    public async ValueTask Handle(FileDeleted notification, CancellationToken ct)
    {
        await Task.Yield();

        if (notification.Id == "fail")
        {
            throw new InvalidOperationException("purge failed");
        }

        journal.Write($"purge {notification.Id}");
    }
}

public sealed class IndexFileEvents(Journal journal) : INotificationHandler<IFileEvent>
{
    public ValueTask Handle(IFileEvent notification, CancellationToken ct)
    {
        journal.Write($"index {notification.Id}");
        return ValueTask.CompletedTask;
    }
}

public sealed class CountEverything(Journal journal) : INotificationHandler<INotification>
{
    public ValueTask Handle(INotification notification, CancellationToken ct)
    {
        journal.Write($"count {notification.GetType().Name}");
        return ValueTask.CompletedTask;
    }
}

// Behaviors.

/// <summary>Open generic: runs for every request.</summary>
public sealed class TraceBehavior<TRequest, TResponse>(Journal journal) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async ValueTask<TResponse> Handle(TRequest request, PipelineNext<TRequest, TResponse> next, CancellationToken ct)
    {
        journal.Write($"trace> {typeof(TRequest).Name}");
        var response = await next(request, ct);
        journal.Write($"trace< {typeof(TRequest).Name}");
        return response;
    }
}

/// <summary>Open generic narrowed by a constraint: runs only for requests answered with a ResultingKit result.</summary>
public sealed class FailureLogBehavior<TRequest, TResponse>(Journal journal) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async ValueTask<TResponse> Handle(TRequest request, PipelineNext<TRequest, TResponse> next, CancellationToken ct)
    {
        var response = await next(request, ct);

        if (response.IsFailure)
        {
            journal.Write($"failed {response.Error!.Code}");
        }

        return response;
    }
}

/// <summary>Closed: validates one request and short-circuits with a failed result.</summary>
public sealed class DeleteFileValidation(Journal journal) : IPipelineBehavior<DeleteFile, Result>
{
    public ValueTask<Result> Handle(DeleteFile request, PipelineNext<DeleteFile, Result> next, CancellationToken ct)
    {
        journal.Write("validate");

        return string.IsNullOrWhiteSpace(request.StorageId)
            ? ValueTask.FromResult(Result.Failure(Error.Validation("file.id_required", "A storage id is required.")))
            : next(request, ct);
    }
}

/// <summary>A pass-through behavior with no async state machine, for the allocation test.</summary>
public sealed class PassThroughBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public ValueTask<TResponse> Handle(TRequest request, PipelineNext<TRequest, TResponse> next, CancellationToken ct) =>
        next(request, ct);
}
