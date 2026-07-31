namespace MessagingKit.Inbox;

public class InboxOptions
{
    public const string SectionName = "Inbox";

    public bool Enabled { get; set; } = true;
    public string Schema { get; set; } = "messaging";
    public string TableName { get; set; } = "inbox";
    public int BatchSize { get; set; } = 50;
    public int Concurrency { get; set; } = 4;
    public int PollIntervalSeconds { get; set; } = 5;
    public int LockDurationSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 10;
    public int BaseRetryDelaySeconds { get; set; } = 10;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
}
