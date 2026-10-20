using Microsoft.EntityFrameworkCore;

namespace MessagingKit.Outbox;

/// <summary>Counts the rows the dispatcher has not finished with, for the backlog instrument.</summary>
internal interface IOutboxBacklog
{
    /// <summary>Rows still to deliver (waiting or in flight), and rows dead-lettered.</summary>
    Task<(long Pending, long Dead)> CountAsync(CancellationToken ct);
}

internal sealed class OutboxBacklog<TContext>(TContext context) : IOutboxBacklog
    where TContext : DbContext
{
    public async Task<(long Pending, long Dead)> CountAsync(CancellationToken ct)
    {
        // One grouped scan over the (status, scheduled_at) index; sent rows are the bulk and are skipped.
        var counts = await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.Status != OutboxStatus.Sent)
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        var pending = counts.Where(c => c.Status is OutboxStatus.Pending or OutboxStatus.Sending).Sum(c => c.Count);
        var dead = counts.Where(c => c.Status == OutboxStatus.Dead).Sum(c => c.Count);

        return (pending, dead);
    }
}
