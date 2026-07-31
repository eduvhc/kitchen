using MessagingKit;

namespace MailingKit;

/// <summary>
/// The message a module sends to have an email delivered. Staged in the sender's own transaction
/// through <c>IOutbox.Add</c>, so the email commits with the work that caused it or not at all.
/// </summary>
/// <remarks>
/// Idempotency, scheduling, and retries are MessagingKit's: the message id deduplicates, the outbox
/// takes <c>sendAt</c>, and the inbox owns the retry ladder. None of them belong on this contract.
/// The wire name is pinned so renaming this record cannot orphan queued messages.
/// </remarks>
[Message("send-email")]
public sealed record SendEmail
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

    /// <summary>Template key. Subject and body are rendered from it when set.</summary>
    public string? Template { get; init; }

    public Dictionary<string, object?> Model { get; init; } = [];

    public List<Attachment> Attachments { get; init; } = [];
    public Dictionary<string, string> Headers { get; init; } = [];

    /// <summary>Free-text label recorded on the send log, for filtering and auditing.</summary>
    public string? Source { get; init; }
}
