using System.Diagnostics.CodeAnalysis;

namespace MediatingKit;

/// <summary>
/// The rest of the pipeline after a behavior: the next behavior, or the handler. It takes the request and token
/// explicitly instead of capturing them, so the mediator builds the chain once and reuses it for every call.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="request">The request to pass on; usually the one the behavior received.</param>
/// <param name="ct">The cancellation token to pass on.</param>
/// <returns>The response.</returns>
public delegate ValueTask<TResponse> PipelineNext<in TRequest, TResponse>(TRequest request, CancellationToken ct);

/// <summary>
/// Wraps the handling of a request: validation, logging, transactions. Register open-generic behaviors to run for
/// every request (use generic constraints to narrow them, such as <c>where TResponse : Result</c>), or closed ones
/// for a single request type. Behaviors run in registration order, the first registered outermost.
/// </summary>
/// <typeparam name="TRequest">The request type. Requests without a response arrive here as <see cref="IRequest{TResponse}"/> of <see cref="Unit"/>.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <example>
/// <code>
/// public sealed class LoggingBehavior&lt;TRequest, TResponse&gt;(ILogger&lt;TRequest&gt; logger)
///     : IPipelineBehavior&lt;TRequest, TResponse&gt; where TRequest : notnull
/// {
///     public async ValueTask&lt;TResponse&gt; Handle(TRequest request, PipelineNext&lt;TRequest, TResponse&gt; next, CancellationToken ct)
///     {
///         logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
///         return await next(request, ct);
///     }
/// }
/// </code>
/// </example>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "VB-only keyword; next is the name every behavior uses.")]
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Handles <paramref name="request"/>, usually by calling <paramref name="next"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The rest of the pipeline. Not calling it short-circuits the handler.</param>
    /// <param name="ct">Cancelled when the caller gives up.</param>
    /// <returns>The response.</returns>
    ValueTask<TResponse> Handle(TRequest request, PipelineNext<TRequest, TResponse> next, CancellationToken ct);
}
