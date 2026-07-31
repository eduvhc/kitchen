namespace MailingKit.Emails.SendEmail;

public record AttachmentDto
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required string Content { get; init; }
    public string? ContentId { get; init; }
}
