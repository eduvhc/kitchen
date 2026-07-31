using System.Text.RegularExpressions;
using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessagingKit.Outbox.Persistence;

public partial class OutboxStore<TContext>(TContext context, TimeProvider clock, IOptions<OutboxOptions> options) : IOutboxStore
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var table = QualifiedTable();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

#pragma warning disable EF1002
        var ids = await context.Database
            .SqlQueryRaw<Guid>($$"""
                SELECT id
                FROM {{table}}
                WHERE (status = {0} AND scheduled_at <= {2})
                   OR (status = {1} AND locked_until IS NOT NULL AND locked_until < {2})
                ORDER BY scheduled_at
                LIMIT {3}
                FOR UPDATE SKIP LOCKED
                """,
                (int)OutboxStatus.Pending,
                (int)OutboxStatus.Sending,
                now,
                batchSize)
            .ToListAsync(ct);
#pragma warning restore EF1002

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return [];
        }

        var claimed = await context.Set<OutboxMessage>().Where(m => ids.Contains(m.Id)).ToListAsync(ct);

        foreach (var message in claimed)
        {
            message.Status = OutboxStatus.Sending;
            message.AttemptCount += 1;
            message.LockedUntil = now.Add(lockDuration);
            message.UpdatedAt = now;
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var message in claimed)
        {
            context.Entry(message).State = EntityState.Detached;
        }

        return claimed;
    }

    public async Task MarkSentAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = clock.GetUtcNow();

        await context.Set<OutboxMessage>()
            .Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Status, OutboxStatus.Sent)
                    .SetProperty(m => m.SentAt, now)
                    .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(m => m.LastError, (string?)null)
                    .SetProperty(m => m.UpdatedAt, now),
                ct);
    }

    public async Task MarkFailedAsync(
        OutboxMessage message,
        string failure,
        bool permanent,
        TimeSpan retryDelay,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var now = clock.GetUtcNow();
        var exhausted = permanent || message.AttemptCount >= message.MaxAttempts;
        var status = exhausted ? OutboxStatus.Dead : OutboxStatus.Pending;
        var scheduledAt = exhausted ? message.ScheduledAt : now.Add(retryDelay);
        var truncated = failure.Length > 4000 ? failure[..4000] : failure;

        await context.Set<OutboxMessage>()
            .Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Status, status)
                    .SetProperty(m => m.ScheduledAt, scheduledAt)
                    .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(m => m.LastError, truncated)
                    .SetProperty(m => m.UpdatedAt, now),
                ct);
    }

    public Task<OutboxMessage?> FindAsync(Guid id, CancellationToken ct = default) =>
        context.Set<OutboxMessage>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

    private string QualifiedTable()
    {
        if (!IdentifierPattern().IsMatch(_options.Schema) || !IdentifierPattern().IsMatch(_options.TableName))
        {
            throw new InvalidOperationException("Outbox schema and table names may contain only letters, digits, and underscores.");
        }

        return $"{_options.Schema}.{_options.TableName}";
    }

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
