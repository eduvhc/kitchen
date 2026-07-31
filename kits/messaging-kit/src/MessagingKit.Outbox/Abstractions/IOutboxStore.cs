using MessagingKit.Outbox.Domain;

namespace MessagingKit.Outbox.Abstractions;

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int batchSize, TimeSpan lockDuration, CancellationToken ct = default);

    Task MarkSentAsync(OutboxMessage message, CancellationToken ct = default);

    Task MarkFailedAsync(OutboxMessage message, string failure, bool permanent, TimeSpan retryDelay, CancellationToken ct = default);

    Task<OutboxMessage?> FindAsync(Guid id, CancellationToken ct = default);
}
