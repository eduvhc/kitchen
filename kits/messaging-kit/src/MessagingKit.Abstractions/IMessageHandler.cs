namespace MessagingKit;

public interface IMessageHandler<in TMessage>
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct = default);
}
