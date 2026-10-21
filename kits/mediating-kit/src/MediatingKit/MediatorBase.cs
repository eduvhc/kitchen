using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace MediatingKit;

/// <summary>
/// The base of the mediator the source generator writes for each project that calls <c>AddMediator()</c>. Not for
/// direct use: the generated subclass holds one typed field per handler and dispatches with type checks against the
/// request types it found at compile time.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class MediatorBase : IMediator
{
    private readonly MediatorRuntime _runtime;

    /// <summary>Creates the mediator over the container or scope it was resolved from.</summary>
    /// <param name="services">The container or scope that resolves handlers and behaviors.</param>
    protected MediatorBase(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Services = services;
        _runtime = services.GetRequiredService<MediatorRuntime>();
    }

    /// <summary>The container or scope that resolves handlers and behaviors.</summary>
    protected IServiceProvider Services { get; }

    /// <summary>Whether a span or metric listener is attached. Checked per call.</summary>
    protected bool IsInstrumented => _runtime.Telemetry.IsActive;

    /// <inheritdoc />
    public abstract ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);

    /// <inheritdoc />
    public abstract ValueTask Send(IRequest request, CancellationToken ct = default);

    /// <inheritdoc />
    public abstract IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default);

    /// <inheritdoc />
    public abstract ValueTask Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification;

    /// <summary>Resolves a handler. Called once per handler per mediator instance.</summary>
    /// <typeparam name="THandler">The handler's concrete type.</typeparam>
    /// <returns>The handler.</returns>
    protected THandler GetHandler<THandler>()
        where THandler : notnull => Services.GetRequiredService<THandler>();

    /// <summary>Resolves the behaviors for a request type and chains them in front of its handler.</summary>
    /// <typeparam name="THandler">The handler's type, kept so the no-behavior path calls it directly.</typeparam>
    /// <typeparam name="TRequest">The request type the handler declares.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">The handler.</param>
    /// <returns>The pipeline, reused for the lifetime of this mediator.</returns>
    protected RequestPipeline<THandler, TRequest, TResponse> CreatePipeline<THandler, TRequest, TResponse>(THandler handler)
        where THandler : class, IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);

        var (invoke, hasBehaviors) = Chain<TRequest, TResponse>(handler.Handle);
        return new RequestPipeline<THandler, TRequest, TResponse>(handler, invoke, hasBehaviors);
    }

    /// <summary>Resolves the behaviors for a request type without a response and chains them in front of its handler.</summary>
    /// <typeparam name="THandler">The handler's type, kept so the no-behavior path calls it directly.</typeparam>
    /// <typeparam name="TRequest">The request type the handler declares.</typeparam>
    /// <param name="handler">The handler.</param>
    /// <returns>The pipeline, reused for the lifetime of this mediator.</returns>
    protected VoidRequestPipeline<THandler, TRequest> CreateVoidPipeline<THandler, TRequest>(THandler handler)
        where THandler : class, IRequestHandler<TRequest>
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(handler);

        var (invoke, hasBehaviors) = Chain<TRequest, Unit>((request, ct) => ToUnit(handler.Handle(request, ct)));
        return new VoidRequestPipeline<THandler, TRequest>(handler, invoke, hasBehaviors);
    }

    /// <summary>Runs a pipeline that has behaviors, or any pipeline while telemetry is listening.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="invoke">The pipeline's entry point.</param>
    /// <param name="request">The request.</param>
    /// <param name="spanName">The span name, precomputed: <c>send DeleteFile</c>.</param>
    /// <param name="messageType">The request type's full name.</param>
    /// <param name="ct">The caller's token.</param>
    /// <returns>The response.</returns>
    protected ValueTask<TResponse> Dispatch<TRequest, TResponse>(
        PipelineNext<TRequest, TResponse> invoke,
        TRequest request,
        string spanName,
        string messageType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoke);

        return _runtime.Telemetry.IsActive
            ? DispatchInstrumented(invoke, request, spanName, messageType, ct)
            : invoke(request, ct);
    }

    /// <summary>Publishes to the handlers the generator found for a notification type.</summary>
    /// <typeparam name="TNotification">The notification type matched.</typeparam>
    /// <param name="handlers">Every handler for the type, its base types and interfaces.</param>
    /// <param name="notification">The notification.</param>
    /// <param name="spanName">The span name, precomputed: <c>publish FileDeleted</c>.</param>
    /// <param name="messageType">The notification type's full name.</param>
    /// <param name="ct">The publisher's token.</param>
    /// <returns>A task that completes when every handler has finished.</returns>
    protected ValueTask PublishToHandlers<TNotification>(
        INotificationHandler<TNotification>[] handlers,
        TNotification notification,
        string spanName,
        string messageType,
        CancellationToken ct)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(handlers);

        return _runtime.Telemetry.IsActive
            ? PublishInstrumented(handlers, notification, spanName, messageType, ct)
            : PublishCore(handlers, notification, ct);
    }

    /// <summary>Adapts a handler's <see cref="ValueTask"/> to the <see cref="Unit"/> response behaviors see.</summary>
    /// <param name="task">The handler's task.</param>
    /// <returns><see cref="Unit.Value"/> once <paramref name="task"/> completes.</returns>
    protected static ValueTask<Unit> ToUnit(ValueTask task)
    {
        if (task.IsCompletedSuccessfully)
        {
            task.GetAwaiter().GetResult();
            return new ValueTask<Unit>(Unit.Value);
        }

        return AwaitUnit(task);

        static async ValueTask<Unit> AwaitUnit(ValueTask task)
        {
            await task.ConfigureAwait(false);
            return Unit.Value;
        }
    }

    /// <summary>Drops the <see cref="Unit"/> from a pipeline's response.</summary>
    /// <param name="task">The pipeline's task.</param>
    /// <returns>A task that completes with <paramref name="task"/>.</returns>
    protected static ValueTask ToValueTask(ValueTask<Unit> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            task.GetAwaiter().GetResult();
            return default;
        }

        return new ValueTask(task.AsTask());
    }

    /// <summary>Throws for a message nothing handles, or for a <see langword="null"/> message.</summary>
    /// <typeparam name="T">The return type of the caller, so it can be used in a <see langword="return"/>.</typeparam>
    /// <param name="message">The message.</param>
    /// <returns>Never returns.</returns>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected static T ThrowNoHandler<T>(object? message)
    {
        ArgumentNullException.ThrowIfNull(message);

        throw new InvalidOperationException(
            $"No handler for '{message.GetType()}' was found when this mediator was generated. Handlers are found at "
            + "compile time in the project that calls AddMediator() and, if public or visible to it, in the projects it "
            + "references. A handler in a project that is neither is invisible to this mediator.");
    }

    private (PipelineNext<TRequest, TResponse> Invoke, bool HasBehaviors) Chain<TRequest, TResponse>(PipelineNext<TRequest, TResponse> handler)
        where TRequest : notnull
    {
        var resolved = Services.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviors = resolved as IPipelineBehavior<TRequest, TResponse>[] ?? [.. resolved];

        var invoke = handler;

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            invoke = Wrap(behaviors[i], invoke);
        }

        return (invoke, behaviors.Length > 0);

        static PipelineNext<TRequest, TResponse> Wrap(IPipelineBehavior<TRequest, TResponse> behavior, PipelineNext<TRequest, TResponse> next) =>
            (request, ct) => behavior.Handle(request, next, ct);
    }

    private async ValueTask<TResponse> DispatchInstrumented<TRequest, TResponse>(
        PipelineNext<TRequest, TResponse> invoke,
        TRequest request,
        string spanName,
        string messageType,
        CancellationToken ct)
    {
        // An async method, so the span set as Activity.Current here never leaks into the caller's context.
        var operation = _runtime.Telemetry.Start(MediatingKitTags.Send, spanName, messageType);

        try
        {
            var response = await invoke(request, ct).ConfigureAwait(false);
            operation.Succeed();
            return response;
        }
        catch (Exception ex)
        {
            operation.Fail(ex);
            throw;
        }
    }

    private async ValueTask PublishInstrumented<TNotification>(
        INotificationHandler<TNotification>[] handlers,
        TNotification notification,
        string spanName,
        string messageType,
        CancellationToken ct)
        where TNotification : INotification
    {
        var operation = _runtime.Telemetry.Start(MediatingKitTags.Publish, spanName, messageType);

        try
        {
            await PublishCore(handlers, notification, ct).ConfigureAwait(false);
            operation.Succeed();
        }
        catch (Exception ex)
        {
            operation.Fail(ex);
            throw;
        }
    }

    private ValueTask PublishCore<TNotification>(INotificationHandler<TNotification>[] handlers, TNotification notification, CancellationToken ct)
        where TNotification : INotification =>
        handlers.Length > 1 && _runtime.NotificationPublishing == NotificationPublishing.Parallel
            ? PublishParallel(handlers, notification, ct)
            : PublishSequential(handlers, notification, ct);

    private static ValueTask PublishSequential<TNotification>(INotificationHandler<TNotification>[] handlers, TNotification notification, CancellationToken ct)
        where TNotification : INotification
    {
        for (var i = 0; i < handlers.Length; i++)
        {
            var pending = handlers[i].Handle(notification, ct);

            if (!pending.IsCompletedSuccessfully)
            {
                return AwaitRemaining(pending, handlers, i + 1, notification, ct);
            }

            pending.GetAwaiter().GetResult();
        }

        return default;

        static async ValueTask AwaitRemaining(
            ValueTask pending,
            INotificationHandler<TNotification>[] handlers,
            int next,
            TNotification notification,
            CancellationToken ct)
        {
            await pending.ConfigureAwait(false);

            for (var i = next; i < handlers.Length; i++)
            {
                await handlers[i].Handle(notification, ct).ConfigureAwait(false);
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every handler runs; failures are rethrown together.")]
    private static async ValueTask PublishParallel<TNotification>(INotificationHandler<TNotification>[] handlers, TNotification notification, CancellationToken ct)
        where TNotification : INotification
    {
        var tasks = new Task[handlers.Length];

        for (var i = 0; i < handlers.Length; i++)
        {
            try
            {
                tasks[i] = handlers[i].Handle(notification, ct).AsTask();
            }
            catch (Exception ex)
            {
                tasks[i] = Task.FromException(ex);
            }
        }

        var all = Task.WhenAll(tasks);

        try
        {
            await all.ConfigureAwait(false);
        }
        catch when (all.Exception is { InnerExceptions.Count: > 1 })
        {
            throw all.Exception;
        }
    }
}
