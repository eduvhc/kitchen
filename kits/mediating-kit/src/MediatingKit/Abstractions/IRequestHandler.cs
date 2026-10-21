namespace MediatingKit;

/// <summary>
/// Handles one request type. The source generator finds every implementation at compile time and registers it; there
/// is nothing to wire by hand.
/// </summary>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">The response declared by <typeparamref name="TRequest"/>.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles <paramref name="request"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancelled when the caller gives up.</param>
    /// <returns>The response.</returns>
    ValueTask<TResponse> Handle(TRequest request, CancellationToken ct);
}

/// <summary>Handles one request type that has no response.</summary>
/// <typeparam name="TRequest">The request handled.</typeparam>
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    /// <summary>Handles <paramref name="request"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancelled when the caller gives up.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    ValueTask Handle(TRequest request, CancellationToken ct);
}

/// <summary>Handles one stream request type.</summary>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">The item type declared by <typeparamref name="TRequest"/>.</typeparam>
public interface IStreamRequestHandler<in TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>Starts the stream for <paramref name="request"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancelled when the caller stops reading.</param>
    /// <returns>The items, produced as the caller enumerates them.</returns>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken ct);
}

/// <summary>
/// Handles a notification type, and through contravariance anything derived from it. Implement it for an interface
/// such as <see cref="INotification"/> to observe every notification.
/// </summary>
/// <typeparam name="TNotification">The notification handled.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Handles <paramref name="notification"/>.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="ct">Cancelled when the publisher gives up.</param>
    /// <returns>A task that completes when the notification has been handled.</returns>
    ValueTask Handle(TNotification notification, CancellationToken ct);
}
