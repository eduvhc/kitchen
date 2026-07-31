namespace MailingKit.Options;

public class DispatcherOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 20;
    public int Concurrency { get; set; } = 4;
    public int PollIntervalSeconds { get; set; } = 5;
    public int LockDurationSeconds { get; set; } = 120;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
}
