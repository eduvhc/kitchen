namespace MailingKit.Emails.SendEmail;

public record SendEmailRequest
{
    public List<string> To { get; init; } = [];
    public List<string> Cc { get; init; } = [];
    public List<string> Bcc { get; init; } = [];

    public string? From { get; init; }
    public string? FromName { get; init; }
    public string? ReplyTo { get; init; }

    public string? Subject { get; init; }
    public string? Html { get; init; }
    public string? Text { get; init; }

    public string? Template { get; init; }
    public Dictionary<string, object?> Model { get; init; } = [];

    public List<AttachmentDto> Attachments { get; init; } = [];
    public Dictionary<string, string> Headers { get; init; } = [];

    public string? IdempotencyKey { get; init; }
    public DateTimeOffset? SendAt { get; init; }

    /// <summary>Optional free-text label recorded on the email, for filtering and auditing.</summary>
    public string? Source { get; init; }

    /// <summary>Overrides the configured default. Must be between 1 and 20.</summary>
    public int? MaxAttempts { get; init; }
}
