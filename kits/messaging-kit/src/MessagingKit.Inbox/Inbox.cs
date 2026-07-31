using MessagingKit.Inbox.Abstractions;
using MessagingKit.Inbox.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessagingKit.Inbox;

public class Inbox<TContext>(TContext context, TimeProvider clock, IOptions<InboxOptions> options) : IInbox
    where TContext : DbContext
{
    private readonly InboxOptions _options = options.Value;

    public async Task<bool> TryStoreAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var now = clock.GetUtcNow();

        if (await context.Set<InboxMessage>().AnyAsync(m => m.Id == envelope.Id, ct))
        {
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
            return true;
        }
        catch (DbUpdateException)
        {
            foreach (var entry in context.ChangeTracker.Entries<InboxMessage>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    public Task<InboxMessage?> FindAsync(Guid id, CancellationToken ct = default) =>
        context.Set<InboxMessage>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
}
