namespace MediatingKit;

/// <summary>Sends requests to their one handler.</summary>
public interface ISender
{
    /// <summary>
    /// Sends <paramref name="request"/> through the pipeline behaviors to its handler. The response type is inferred
    /// from the request: <c>await mediator.Send(new DeleteFile(id), ct)</c>.
    /// </summary>
    /// <typeparam name="TResponse">The response type the request declares.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="ct">Passed to the behaviors and the handler.</param>
    /// <returns>The handler's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No handler for the request's type was found at compile time.</exception>
    ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);

    /// <summary>Sends a request that has no response through the pipeline behaviors to its handler.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Passed to the behaviors and the handler.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No handler for the request's type was found at compile time.</exception>
    ValueTask Send(IRequest request, CancellationToken ct = default);

    /// <summary>Starts the stream for <paramref name="request"/> from its handler.</summary>
    /// <typeparam name="TResponse">The item type the request declares.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="ct">Passed to the handler.</param>
    /// <returns>The items, produced as they are enumerated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No handler for the request's type was found at compile time.</exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default);
}

/// <summary>Publishes notifications to every handler for them.</summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes <paramref name="notification"/> to every handler for its type, its base types and its interfaces.
    /// Handlers run one after another by default; see <see cref="MediatorOptions.NotificationPublishing"/>.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification.</param>
    /// <param name="ct">Passed to every handler.</param>
    /// <returns>A task that completes when every handler has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is <see langword="null"/>.</exception>
    ValueTask Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification;
}

/// <summary>
/// The mediator: sends requests and publishes notifications. Registered by <c>services.AddMediator()</c>; the
/// implementation is generated at compile time for the project that calls it.
/// </summary>
public interface IMediator : ISender, IPublisher;
