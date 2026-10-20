using MessagingKit.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessagingKit.Inbox;

/// <summary>Stores received messages once each, keyed by message id, for the processor to handle.</summary>
/// <remarks>
/// <c>AddInbox</c> registers one that reports <c>messagingkit.inbox.received.messages</c>; an instance built
/// with this public constructor reports no metrics.
/// </remarks>
public class Inbox<TContext>(TContext context, TimeProvider clock, IOptions<InboxOptions> options) : IInbox
    where TContext : DbContext
{
    private readonly InboxOptions _options = options.Value;
    private readonly InboxMetrics? _metrics;

    internal Inbox(TContext context, TimeProvider clock, IOptions<InboxOptions> options, InboxMetrics metrics)
        : this(context, clock, options)
    {
        _metrics = metrics;
    }

    public async Task<bool> TryStoreAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var now = clock.GetUtcNow();

        if (await context.Set<InboxMessage>().AnyAsync(m => m.Id == envelope.Id, ct))
        {
            _metrics?.RecordReceived(envelope.Type, InboxMetrics.Duplicate);
            return false;
        }

        context.Set<InboxMessage>().Add(new InboxMessage
        {
            Id = envelope.Id,
            Type = envelope.Type,
            Payload = envelope.Payload,
            Headers = new Dictionary<string, string>(envelope.Headers),
            Status = InboxStatus.Pending,
            MaxAttempts = _options.MaxAttempts,
            ScheduledAt = now,
            ReceivedAt = now,
            UpdatedAt = now,
        });

        try
        {
            await context.SaveChangesAsync(ct);
            _metrics?.RecordReceived(envelope.Type, InboxMetrics.Stored);
            return true;
        }
        catch (DbUpdateException)
        {
            foreach (var entry in context.ChangeTracker.Entries<InboxMessage>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            _metrics?.RecordReceived(envelope.Type, InboxMetrics.Duplicate);
            return false;
        }
    }

    public Task<InboxMessage?> FindAsync(Guid id, CancellationToken ct = default) =>
        context.Set<InboxMessage>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
}
