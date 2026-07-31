namespace MailingKit.Emails.Domain;

public class EmailAttachment
{
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required string Content { get; set; }
    public string? ContentId { get; set; }
}
