namespace MailingKit.Sending;

/// <summary>
/// A fully resolved email, ready for a transport. Templates are already rendered and recipients
/// validated, so a transport only has to move bytes.
/// </summary>
public sealed class OutgoingEmail
{
    /// <summary>
    /// Identifies this send across retries: every attempt at the same inbox message carries the same
    /// value, and different messages never share one. A transport passes it to its provider to make a
    /// retried send idempotent, so an email the provider accepted before its answer was lost is not
    /// delivered twice.
    /// </summary>
    /// <remarks>
    /// <see cref="SendEmail"/> handling sets it from the inbox message id; it is at most 64 characters.
    /// Null when the email was built by hand, in which case a transport sends without one.
    /// </remarks>
    public string? IdempotencyKey { get; init; }

    /// <summary>The sender's address.</summary>
    public required string FromAddress { get; init; }

    /// <summary>The sender's display name, if any.</summary>
    public string? FromName { get; init; }

    /// <summary>The address replies go to, if not the sender.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>Primary recipients. Never empty.</summary>
    public required List<string> To { get; init; }

    /// <summary>Carbon-copy recipients.</summary>
    public List<string> Cc { get; init; } = [];

    /// <summary>Blind carbon-copy recipients.</summary>
    public List<string> Bcc { get; init; } = [];

    /// <summary>The rendered subject.</summary>
    public required string Subject { get; init; }

    /// <summary>The rendered HTML body. At least one of this and <see cref="TextBody"/> is set.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>The rendered plain-text body.</summary>
    public string? TextBody { get; init; }

    /// <summary>Attachments, base64-encoded. One with a content id is an inline image.</summary>
    public List<EmailAttachment> Attachments { get; init; } = [];

    /// <summary>Extra message headers, sent as given.</summary>
    public Dictionary<string, string> Headers { get; init; } = [];
}
