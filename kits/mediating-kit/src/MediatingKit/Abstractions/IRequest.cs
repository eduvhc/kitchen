using System.Diagnostics.CodeAnalysis;

namespace MediatingKit;

/// <summary>
/// A request answered by exactly one <see cref="IRequestHandler{TRequest, TResponse}"/>. The response type is declared
/// here, once, so <c>mediator.Send(request)</c> infers it.
/// </summary>
/// <typeparam name="TResponse">What the handler returns, such as a ResultingKit <c>Result</c>.</typeparam>
/// <remarks>
/// Deliberately invariant: a request has one response type, and the generated dispatch relies on it. Implementing
/// more than one request interface on a type is the compile-time error MED1003.
/// </remarks>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker carrying the response type for inference.")]
public interface IRequest<TResponse>;

/// <summary>
/// A request with no response, answered by exactly one <see cref="IRequestHandler{TRequest}"/>. Behaviors see it as
/// an <see cref="IRequest{TResponse}"/> of <see cref="Unit"/>, so one open-generic behavior covers both kinds.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker for requests without a response.")]
public interface IRequest : IRequest<Unit>;

/// <summary>
/// A request answered by a stream of items from exactly one <see cref="IStreamRequestHandler{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker carrying the item type for inference.")]
public interface IStreamRequest<TResponse>;

/// <summary>
/// Something that happened, published to every <see cref="INotificationHandler{TNotification}"/> for its type, for
/// any base type it derives from, and for any interface it implements. Zero handlers is valid.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker for notifications.")]
public interface INotification;
