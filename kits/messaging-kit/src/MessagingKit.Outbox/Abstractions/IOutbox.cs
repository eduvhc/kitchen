using MessagingKit.Outbox.Domain;

namespace MessagingKit.Outbox.Abstractions;

public interface IOutbox
{
    OutboxMessage Add<TMessage>(
        TMessage message,
        string? destination = null,
        IDictionary<string, string>? headers = null,
        DateTimeOffset? sendAt = null)
        where TMessage : notnull;
}
