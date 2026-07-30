using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessagingKit.Outbox;

public class Outbox<TContext>(
    TContext context,
    IMessageSerializer serializer,
    MessageTypeRegistry registry,
    TimeProvider clock,
    IOptions<OutboxOptions> options) : IOutbox
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;

    public OutboxMessage Add<TMessage>(
        TMessage message,
        string? destination = null,
        IDictionary<string, string>? headers = null,
        DateTimeOffset? sendAt = null)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);

        var now = clock.GetUtcNow();

        var row = new OutboxMessage
        {
            Type = registry.NameOf(typeof(TMessage)),
            Payload = serializer.Serialize(message),
            Destination = destination,
            Headers = headers is null ? [] : new Dictionary<string, string>(headers),
            Status = OutboxStatus.Pending,
            MaxAttempts = _options.MaxAttempts,
            ScheduledAt = sendAt ?? now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Set<OutboxMessage>().Add(row);
        return row;
    }
}
