namespace MessagingKit.Outbox.Domain;

public enum OutboxStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Dead = 3,
}
