using EmailService.Features.Emails.Domain;

namespace EmailService.Features.Emails.Abstractions;

public interface IEmailQueue
{
    Task<EmailMessage> EnqueueAsync(EmailMessage message, CancellationToken ct = default);

    Task<EmailMessage?> FindAsync(Guid id, CancellationToken ct = default);

    Task<EmailMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<EmailMessage>> ListAsync(EmailQueryFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<EmailMessage>> ClaimBatchAsync(int batchSize, TimeSpan lockDuration, CancellationToken ct = default);

    Task MarkSentAsync(EmailMessage message, string? providerMessageId, CancellationToken ct = default);

    Task MarkFailedAsync(EmailMessage message, string error, bool permanent, TimeSpan retryDelay, CancellationToken ct = default);

    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);
}
