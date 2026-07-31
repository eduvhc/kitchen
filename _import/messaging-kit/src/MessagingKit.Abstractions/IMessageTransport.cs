namespace MessagingKit;

public interface IMessageTransport
{
    Task<TransportResult> SendAsync(MessageEnvelope envelope, CancellationToken ct = default);
}
