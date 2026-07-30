using EmailService.Features.Emails;

namespace EmailService.Tests.TestDoubles;

public class FakeEmailQueue : IEmailQueue
{
    public List<EmailMessage> Messages { get; } = [];

    public Task<EmailMessage> EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        Messages.Add(message);
        return Task.FromResult(message);
    }

    public Task<EmailMessage?> FindAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Messages.FirstOrDefault(m => m.Id == id));

    public Task<EmailMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Messages.FirstOrDefault(m => m.IdempotencyKey == key));

    public Task<IReadOnlyList<EmailMessage>> ListAsync(EmailQueryFilter filter, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmailMessage>>(Messages);

    public Task<IReadOnlyList<EmailMessage>> ClaimBatchAsync(int batchSize, TimeSpan lockDuration, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmailMessage>>([]);

    public Task MarkSentAsync(EmailMessage message, string? providerMessageId, CancellationToken ct = default)
    {
        message.Status = EmailStatus.Sent;
        message.ProviderMessageId = providerMessageId;
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(EmailMessage message, string error, bool permanent, TimeSpan retryDelay, CancellationToken ct = default)
    {
        message.Status = permanent ? EmailStatus.Dead : EmailStatus.Queued;
        message.LastError = error;
        return Task.CompletedTask;
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var message = Messages.FirstOrDefault(m => m.Id == id);
        if (message is null || message.Status != EmailStatus.Queued)
        {
            return Task.FromResult(false);
        }

        message.Status = EmailStatus.Cancelled;
        return Task.FromResult(true);
    }
}
