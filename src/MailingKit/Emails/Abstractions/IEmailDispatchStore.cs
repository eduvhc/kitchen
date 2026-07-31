using MailingKit.Emails.Domain;

namespace MailingKit.Emails.Abstractions;

/// <summary>
/// Dispatcher-only state transitions. Deliberately not on <see cref="IEmailQueue"/> — products have
/// no business claiming batches or marking delivery outcomes.
/// </summary>
internal interface IEmailDispatchStore
{
    Task<IReadOnlyList<EmailMessage>> ClaimBatchAsync(int batchSize, TimeSpan lockDuration, CancellationToken ct = default);

    Task MarkSentAsync(EmailMessage message, string? providerMessageId, CancellationToken ct = default);

    Task MarkFailedAsync(EmailMessage message, string error, bool permanent, TimeSpan retryDelay, CancellationToken ct = default);
}
