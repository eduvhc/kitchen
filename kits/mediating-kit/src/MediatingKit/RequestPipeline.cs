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
    internal RequestPipeline(THandler handler, PipelineNext<TRequest, TResponse> invoke, bool hasBehaviors)
    {
        Handler = handler;
        Invoke = invoke;
        HasBehaviors = hasBehaviors;
    }

    /// <summary>The handler.</summary>
    public THandler Handler { get; }

    /// <summary>The first behavior, or the handler itself when there are none.</summary>
    public PipelineNext<TRequest, TResponse> Invoke { get; }

    /// <summary>Whether any behavior applies; when not, the handler can be called directly.</summary>
    public bool HasBehaviors { get; }
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
    internal VoidRequestPipeline(THandler handler, PipelineNext<TRequest, Unit> invoke, bool hasBehaviors)
    {
        Handler = handler;
        Invoke = invoke;
        HasBehaviors = hasBehaviors;
    }

    /// <summary>The handler.</summary>
    public THandler Handler { get; }

    /// <summary>The first behavior, or the handler itself when there are none.</summary>
    public PipelineNext<TRequest, Unit> Invoke { get; }

    /// <summary>Whether any behavior applies; when not, the handler can be called directly.</summary>
    public bool HasBehaviors { get; }
}
