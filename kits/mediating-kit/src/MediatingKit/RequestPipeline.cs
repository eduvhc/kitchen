using System.ComponentModel;

namespace MediatingKit;

/// <summary>
/// One request type's handler and the behaviors chained in front of it, built once per mediator instance. Not for
/// direct use. <typeparamref name="THandler"/> is the handler's concrete type, so with no behaviors the generated code
/// calls it directly instead of through an interface or delegate.
/// </summary>
/// <typeparam name="THandler">The handler's type.</typeparam>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RequestPipeline<THandler, TRequest, TResponse>
    where THandler : class, IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    internal RequestPipeline(THandler handler, PipelineNext<TRequest, TResponse> invoke, IPipelineBehavior<TRequest, TResponse>? first, PipelineNext<TRequest, TResponse> next)
    {
        Handler = handler;
        Invoke = invoke;
        First = first;
        Next = next;
    }

    /// <summary>The handler.</summary>
    public THandler Handler { get; }

    /// <summary>The whole chain: every behavior, then the handler. Used when telemetry wraps the call.</summary>
    public PipelineNext<TRequest, TResponse> Invoke { get; }

    /// <summary>The outermost behavior, or <see langword="null"/> when none applies and the handler is called directly.</summary>
    public IPipelineBehavior<TRequest, TResponse>? First { get; }

    /// <summary>The chain after <see cref="First"/>: the <c>next</c> it is called with.</summary>
    public PipelineNext<TRequest, TResponse> Next { get; }
}

/// <summary>
/// One request type's handler and the behaviors chained in front of it, for requests without a response. Not for
/// direct use.
/// </summary>
/// <typeparam name="THandler">The handler's type.</typeparam>
/// <typeparam name="TRequest">The request type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class VoidRequestPipeline<THandler, TRequest>
    where THandler : class, IRequestHandler<TRequest>
    where TRequest : IRequest
{
    internal VoidRequestPipeline(THandler handler, PipelineNext<TRequest, Unit> invoke, IPipelineBehavior<TRequest, Unit>? first, PipelineNext<TRequest, Unit> next)
    {
        Handler = handler;
        Invoke = invoke;
        First = first;
        Next = next;
    }

    /// <summary>The handler.</summary>
    public THandler Handler { get; }

    /// <summary>The whole chain: every behavior, then the handler. Used when telemetry wraps the call.</summary>
    public PipelineNext<TRequest, Unit> Invoke { get; }

    /// <summary>The outermost behavior, or <see langword="null"/> when none applies and the handler is called directly.</summary>
    public IPipelineBehavior<TRequest, Unit>? First { get; }

    /// <summary>The chain after <see cref="First"/>: the <c>next</c> it is called with.</summary>
    public PipelineNext<TRequest, Unit> Next { get; }
}
