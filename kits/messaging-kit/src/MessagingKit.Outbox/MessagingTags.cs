namespace MessagingKit;

// Attribute names shared by spans and metrics on both halves. Compiled into the outbox and linked into
// the inbox, so the two cannot spell a name differently. Names outside the messaging.* and error.*
// semantic conventions carry the messagingkit. prefix.
internal static class MessagingTags
{
    public const string System = "messaging.system";
    public const string SystemValue = "messagingkit";
    public const string OperationName = "messaging.operation.name";
    public const string OperationType = "messaging.operation.type";
    public const string MessageId = "messaging.message.id";
    public const string DestinationName = "messaging.destination.name";
    public const string ErrorType = "error.type";

    public const string MessageType = "messagingkit.message.type";
    public const string Attempt = "messagingkit.message.attempt";
    public const string Outcome = "messagingkit.outcome";
    public const string MessageStatus = "messagingkit.message.status";

    public const string Pending = "pending";
    public const string Dead = "dead";

    // The fallback the error.type convention defines for a failure with no better name.
    public const string OtherError = "_OTHER";
}
