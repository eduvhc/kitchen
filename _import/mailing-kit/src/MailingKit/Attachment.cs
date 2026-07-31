namespace MailingKit;

/// <param name="Content">Base64. Travels inside the message payload, so keep attachments small.</param>
/// <param name="ContentId">Set to embed as an inline resource, referenced from the HTML as <c>cid:</c>.</param>
public sealed record Attachment
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required string Content { get; init; }
    public string? ContentId { get; init; }
}
