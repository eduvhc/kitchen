using ResultingKit;

namespace MessagingKit.Abstractions;

/// <summary>Delivers outbox messages to their destination, such as a broker or another module's inbox.</summary>
public interface IMessageTransport
{
    /// <summary>Delivers one envelope.</summary>
    /// <param name="envelope">The message to deliver.</param>
    /// <param name="ct">Cancels the delivery.</param>
    /// <returns>
    /// A success when the destination has the message, including when it already had it. On failure, an
    /// error of kind <see cref="ErrorKind.Transient"/> is retried with backoff; any other kind sends the
    /// message straight to dead-letter.
    /// </returns>
    Task<Result> SendAsync(MessageEnvelope envelope, CancellationToken ct = default);
}
