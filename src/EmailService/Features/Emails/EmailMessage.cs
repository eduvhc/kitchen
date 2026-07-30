namespace EmailService.Features.Emails;

public class EmailMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string? IdempotencyKey { get; set; }
    public string? Source { get; set; }

    public required string FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ReplyTo { get; set; }

    public required List<string> To { get; set; } = [];
    public List<string> Cc { get; set; } = [];
    public List<string> Bcc { get; set; } = [];

    public required string Subject { get; set; }
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }

    public List<EmailAttachment> Attachments { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];

    public string? TemplateKey { get; set; }

    public EmailStatus Status { get; set; } = EmailStatus.Queued;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;

    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public string? ProviderMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class EmailAttachment
{
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required string Content { get; set; }
    public string? ContentId { get; set; }
}

public enum EmailStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Dead = 4,
    Cancelled = 5,
}
