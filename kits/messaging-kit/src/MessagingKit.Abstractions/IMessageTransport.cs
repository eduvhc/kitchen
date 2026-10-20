using ResultingKit;

namespace MessagingKit;

public interface IMessageTransport
{
    /// <summary>
    /// Delivers one envelope. A failure whose error is <see cref="ErrorKind.Transient"/> is retried on
    /// the outbox's ladder; any other kind dead-letters the message straight away.
    /// </summary>
    Task<Result> SendAsync(MessageEnvelope envelope, CancellationToken ct = default);
}
