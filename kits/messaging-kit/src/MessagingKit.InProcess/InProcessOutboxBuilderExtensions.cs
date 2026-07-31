using MessagingKit.Outbox;

namespace MessagingKit.InProcess;

public static class InProcessOutboxBuilderExtensions
{
    /// <summary>
    /// Routes messages to the inbox in this process. Pass message type names to route only those;
    /// pass none to make it the default transport.
    /// </summary>
    public static OutboxBuilder UseInProcessTransport(this OutboxBuilder builder, params string[] messageTypes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseTransport<InProcessTransport>(messageTypes);
    }
}
