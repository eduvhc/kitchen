
namespace MessagingKit.Outbox;

public interface IOutbox
{
    OutboxMessage Add<TMessage>(
        TMessage message,
        string? destination = null,
        IDictionary<string, string>? headers = null,
        DateTimeOffset? sendAt = null)
        where TMessage : notnull;
}
