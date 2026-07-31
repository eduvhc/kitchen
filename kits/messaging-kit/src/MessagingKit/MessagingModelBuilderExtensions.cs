using MessagingKit.Inbox.Persistence;
using MessagingKit.Outbox.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MessagingKit;

public static class MessagingModelBuilderExtensions
{
    /// <summary>
    /// Maps both the outbox and inbox tables. Call once from the host's <c>OnModelCreating</c> —
    /// the tables are shared by every module in the host, routed by message type.
    /// </summary>
    public static ModelBuilder AddMessaging(this ModelBuilder builder, string schema = "messaging")
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddOutbox(schema);
        builder.AddInbox(schema);

        return builder;
    }
}
