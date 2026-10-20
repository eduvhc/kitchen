
namespace MessagingKit.Inbox;

public interface IInboxStore
{
    Task<IReadOnlyList<InboxMessage>> ClaimBatchAsync(int batchSize, TimeSpan lockDuration, CancellationToken ct = default);

    Task MarkProcessedAsync(InboxMessage message, CancellationToken ct = default);

    Task MarkFailedAsync(InboxMessage message, string failure, bool permanent, TimeSpan retryDelay, CancellationToken ct = default);
}
