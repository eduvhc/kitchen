using MessagingKit.Inbox.Domain;

namespace MessagingKit.Inbox.Abstractions;

public interface IInbox
{
    Task<bool> TryStoreAsync(MessageEnvelope envelope, CancellationToken ct = default);

    Task<InboxMessage?> FindAsync(Guid id, CancellationToken ct = default);
}
