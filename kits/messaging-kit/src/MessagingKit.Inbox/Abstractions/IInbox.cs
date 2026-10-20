using MessagingKit.Abstractions;

namespace MessagingKit.Inbox;

public interface IInbox
{
    Task<bool> TryStoreAsync(MessageEnvelope envelope, CancellationToken ct = default);

    Task<InboxMessage?> FindAsync(Guid id, CancellationToken ct = default);
}
