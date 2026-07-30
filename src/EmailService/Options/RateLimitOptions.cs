namespace EmailService.Options;

public class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 120;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; }
    public Dictionary<string, SourceRateLimit> Sources { get; set; } = [];
}

public class SourceRateLimit
{
    public int? PermitLimit { get; set; }
    public int? WindowSeconds { get; set; }
    public int? QueueLimit { get; set; }
}
