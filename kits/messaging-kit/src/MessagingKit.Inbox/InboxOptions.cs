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

    /// <summary>
    /// How often, at most, the processor counts pending and dead rows for the
    /// <c>messagingkit.inbox.messages</c> instrument. The count runs on the processor's own loop and
    /// only while something listens to that instrument; collection reads the last count. <c>0</c> or less
    /// disables it.
    /// </summary>
    public int BacklogSampleIntervalSeconds { get; set; } = 30;
}
