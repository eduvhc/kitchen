namespace MessagingKit.Outbox.Domain;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Type { get; set; }

    public required string Payload { get; set; }

    public string? Destination { get; set; }

    public Dictionary<string, string> Headers { get; set; } = [];

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 10;

    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
