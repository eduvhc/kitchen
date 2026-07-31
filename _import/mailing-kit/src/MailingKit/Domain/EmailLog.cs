namespace MailingKit.Domain;

/// <summary>
/// A record of what was sent. Not a queue — MessagingKit's outbox and inbox own durability, retries,
/// and dead-lettering, so there is no status machine or locking here.
/// </summary>
/// <remarks>
/// Kept as its own table rather than read back out of the inbox payload because recipients, subject,
/// template, and the provider's message id are what people actually query when asking whether a
/// customer was mailed.
/// </remarks>
public class EmailLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The MessagingKit message that produced this send. Deduplicates a redelivered handle.</summary>
    public Guid MessageId { get; set; }

    public string? Source { get; set; }

    public required string FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ReplyTo { get; set; }

    public required List<string> To { get; set; } = [];
    public List<string> Cc { get; set; } = [];
    public List<string> Bcc { get; set; } = [];

    public required string Subject { get; set; }

    public string? TemplateKey { get; set; }

    public EmailStatus Status { get; set; }
    public int AttemptCount { get; set; }

    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public string? ProviderMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
