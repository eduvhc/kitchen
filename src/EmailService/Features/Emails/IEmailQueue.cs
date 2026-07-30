namespace EmailService.Features.Emails;

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

public record EmailQueryFilter(
    EmailStatus? Status = null,
    string? Recipient = null,
    string? TemplateKey = null,
    string? Source = null,
    int Limit = 50,
    int Offset = 0);
