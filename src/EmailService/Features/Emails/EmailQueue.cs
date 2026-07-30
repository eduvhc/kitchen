using EmailService.Features.Emails.Abstractions;
using EmailService.Features.Emails.Domain;
using EmailService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmailService.Features.Emails;

public class EmailQueue(EmailDbContext db, TimeProvider clock) : IEmailQueue
{
    public async Task<EmailMessage> EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        db.Emails.Add(message);
        await db.SaveChangesAsync(ct);
        return message;
    }

    public Task<EmailMessage?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Emails.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<EmailMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default) =>
        db.Emails.AsNoTracking().FirstOrDefaultAsync(e => e.IdempotencyKey == key, ct);

    public async Task<IReadOnlyList<EmailMessage>> ListAsync(EmailQueryFilter filter, CancellationToken ct = default)
    {
        var query = db.Emails.AsNoTracking().AsQueryable();

        if (filter.Status is { } status)
        {
            query = query.Where(e => e.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Recipient))
        {
            query = query.Where(e => e.To.Contains(filter.Recipient));
        }

        if (!string.IsNullOrWhiteSpace(filter.TemplateKey))
        {
            query = query.Where(e => e.TemplateKey == filter.TemplateKey);
        }

        if (!string.IsNullOrWhiteSpace(filter.Source))
        {
            query = query.Where(e => e.Source == filter.Source);
        }

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(filter.Offset)
            .Take(Math.Clamp(filter.Limit, 1, 200))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmailMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id
                FROM email.emails
                WHERE (status = {(int)EmailStatus.Queued} AND scheduled_at <= {now})
                   OR (status = {(int)EmailStatus.Sending} AND locked_until IS NOT NULL AND locked_until < {now})
                ORDER BY scheduled_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return [];
        }

        var claimed = await db.Emails.Where(e => ids.Contains(e.Id)).ToListAsync(ct);

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

        await db.Emails
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

        await db.Emails
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

        var affected = await db.Emails
            .Where(e => e.Id == id && e.Status == EmailStatus.Queued)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Status, EmailStatus.Cancelled)
                    .SetProperty(e => e.UpdatedAt, now),
                ct);

        return affected > 0;
    }
}
