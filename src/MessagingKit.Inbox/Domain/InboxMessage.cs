namespace MessagingKit.Inbox.Domain;

public class InboxMessage
{
    public required Guid Id { get; set; }

    public required string Type { get; set; }

    public required string Payload { get; set; }

    public Dictionary<string, string> Headers { get; set; } = [];

    public InboxStatus Status { get; set; } = InboxStatus.Pending;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 10;

    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
