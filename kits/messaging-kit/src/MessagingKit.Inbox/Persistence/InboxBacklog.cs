using Microsoft.EntityFrameworkCore;

namespace MessagingKit.Inbox;

/// <summary>Counts the rows the processor has not finished with, for the backlog instrument.</summary>
internal interface IInboxBacklog
{
    /// <summary>Rows still to handle (waiting or in flight), and rows dead-lettered.</summary>
    Task<(long Pending, long Dead)> CountAsync(CancellationToken ct);
}

internal sealed class InboxBacklog<TContext>(TContext context) : IInboxBacklog
    where TContext : DbContext
{
    public async Task<(long Pending, long Dead)> CountAsync(CancellationToken ct)
    {
        // One grouped scan over the (status, scheduled_at) index; processed rows are the bulk and are skipped.
        var counts = await context.Set<InboxMessage>()
            .AsNoTracking()
            .Where(m => m.Status != InboxStatus.Processed)
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        var pending = counts.Where(c => c.Status is InboxStatus.Pending or InboxStatus.Processing).Sum(c => c.Count);
        var dead = counts.Where(c => c.Status == InboxStatus.Dead).Sum(c => c.Count);

        return (pending, dead);
    }
}
