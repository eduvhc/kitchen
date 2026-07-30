namespace MessagingKit.Outbox;

public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; } = true;
    public string Schema { get; set; } = "messaging";
    public string TableName { get; set; } = "outbox";
    public int BatchSize { get; set; } = 50;
    public int Concurrency { get; set; } = 4;
    public int PollIntervalSeconds { get; set; } = 5;
    public int LockDurationSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 10;
    public int BaseRetryDelaySeconds { get; set; } = 10;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
}
