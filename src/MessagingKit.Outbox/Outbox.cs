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
    OutboxSignal signal,
    IOptions<OutboxOptions> options) : IOutbox
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;
    private bool _armed;

    public OutboxMessage Add<TMessage>(
        TMessage message,
        string? destination = null,
        IDictionary<string, string>? headers = null,
        DateTimeOffset? sendAt = null)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);

        var now = clock.GetUtcNow();

        var messageHeaders = headers is null ? [] : new Dictionary<string, string>(headers);

        // Captured here rather than at delivery: the caller's trace is the one worth linking to, and
        // by the time the dispatcher picks the row up that trace is long finished.
        MessagingDiagnostics.InjectTraceContext(messageHeaders);

        var row = new OutboxMessage
        {
            Type = registry.NameOf(typeof(TMessage)),
            Payload = serializer.Serialize(message),
            Destination = destination,
            Headers = messageHeaders,
            Status = OutboxStatus.Pending,
            MaxAttempts = _options.MaxAttempts,
            ScheduledAt = sendAt ?? now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Set<OutboxMessage>().Add(row);
        ArmSignal();

        return row;
    }

    /// <summary>
    /// Wakes the dispatcher once the caller's transaction commits, so a message does not sit out the
    /// poll interval. Nothing fires if the transaction rolls back — the row never existed.
    /// </summary>
    private void ArmSignal()
    {
        if (_armed)
        {
            return;
        }

        _armed = true;
        context.SavedChanges += OnSavedChanges;
    }

    private void OnSavedChanges(object? sender, SavedChangesEventArgs e)
    {
        context.SavedChanges -= OnSavedChanges;
        _armed = false;

        signal.Pulse();
    }
}
