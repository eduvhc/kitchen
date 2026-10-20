using ResultingKit;

namespace MessagingKit;

public interface IMessageTransport
{
    Task<Result> SendAsync(MessageEnvelope envelope, CancellationToken ct = default);
}
