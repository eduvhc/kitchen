using MailingKit.Emails.Abstractions;
using MailingKit.Emails.Domain;
using MailingKit.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MailingKit.Emails;

/// <summary>
/// Backed by the host's <typeparamref name="TContext"/>, so staged emails join whatever transaction
/// the caller is already in.
/// </summary>
internal sealed class EmailQueue<TContext>(
    TContext db,
    TimeProvider clock,
    IOptions<MailingKitOptions> options) : IEmailQueue, IEmailDispatchStore
    where TContext : DbContext
{
    private readonly string _schema = options.Value.Schema;

    private DbSet<EmailMessage> Emails => db.Set<EmailMessage>();

    public void Enqueue(EmailMessage message) => Emails.Add(message);

    public Task<EmailMessage?> FindAsync(Guid id, CancellationToken ct = default) =>
        Emails.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<EmailMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default) =>
        Emails.AsNoTracking().FirstOrDefaultAsync(e => e.IdempotencyKey == key, ct);

    public async Task<IReadOnlyList<EmailMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        // The schema is an identifier, so it cannot be a parameter. MailingKitOptions.Schema
        // validates it to [A-Za-z0-9_] on assignment; nothing caller-supplied reaches this string.
        var sql = $"""
            SELECT id AS "Value"
            FROM "{_schema}".emails
            WHERE (status = {{0}} AND scheduled_at <= {{1}})
               OR (status = {{2}} AND locked_until IS NOT NULL AND locked_until < {{3}})
            ORDER BY scheduled_at
            LIMIT {{4}}
            FOR UPDATE SKIP LOCKED
            """;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var ids = await db.Database
            .SqlQueryRaw<Guid>(
                sql,
                (int)EmailStatus.Queued,
                now,
                (int)EmailStatus.Sending,
                now,
                batchSize)
            .ToListAsync(ct);

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return [];
        }

        var claimed = await Emails.Where(e => ids.Contains(e.Id)).ToListAsync(ct);

        foreach (var message in claimed)
        {
            message.Status = EmailStatus.Sending;
            message.AttemptCount += 1;
            message.LockedUntil = now.Add(lockDuration);
            message.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var message in claimed)
        {
            db.Entry(message).State = EntityState.Detached;
        }

        return claimed;
    }

    public async Task MarkSentAsync(EmailMessage message, string? providerMessageId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        await Emails
            .Where(e => e.Id == message.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Status, EmailStatus.Sent)
                    .SetProperty(e => e.SentAt, now)
                    .SetProperty(e => e.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(e => e.LastError, (string?)null)
                    .SetProperty(e => e.ProviderMessageId, providerMessageId)
                    .SetProperty(e => e.UpdatedAt, now),
                ct);
    }

    public async Task MarkFailedAsync(
        EmailMessage message,
        string error,
        bool permanent,
        TimeSpan retryDelay,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var exhausted = permanent || message.AttemptCount >= message.MaxAttempts;

        var status = exhausted ? EmailStatus.Dead : EmailStatus.Queued;
        var scheduledAt = exhausted ? message.ScheduledAt : now.Add(retryDelay);
        var truncated = error.Length > 4000 ? error[..4000] : error;

        await Emails
            .Where(e => e.Id == message.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Status, status)
                    .SetProperty(e => e.ScheduledAt, scheduledAt)
                    .SetProperty(e => e.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(e => e.LastError, truncated)
                    .SetProperty(e => e.UpdatedAt, now),
                ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var affected = await Emails
            .Where(e => e.Id == id && e.Status == EmailStatus.Queued)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Status, EmailStatus.Cancelled)
                    .SetProperty(e => e.UpdatedAt, now),
                ct);

        return affected > 0;
    }
}
