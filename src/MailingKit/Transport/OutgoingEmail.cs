using MailingKit.Domain;

namespace MailingKit.Transport;

/// <summary>
/// A fully resolved email, ready for a transport. Templates are already rendered and recipients
/// validated, so a transport only has to move bytes.
/// </summary>
public sealed class OutgoingEmail
{
    public required string FromAddress { get; init; }
    public string? FromName { get; init; }
    public string? ReplyTo { get; init; }

    public required List<string> To { get; init; }
    public List<string> Cc { get; init; } = [];
    public List<string> Bcc { get; init; } = [];

    public required string Subject { get; init; }
    public string? HtmlBody { get; init; }
    public string? TextBody { get; init; }

    public List<EmailAttachment> Attachments { get; init; } = [];
    public Dictionary<string, string> Headers { get; init; } = [];
}
