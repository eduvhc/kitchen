namespace MailingKit.Options;

public class EmailDefaultsOptions
{
    public const string SectionName = "EmailDefaults";

    public string FromAddress { get; set; } = "no-reply@localhost";
    public string? FromName { get; set; }
    public string? ReplyTo { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public int MaxRecipients { get; set; } = 50;
    public int MaxAttachmentBytes { get; set; } = 10 * 1024 * 1024;
    public List<string> AllowedRecipientDomains { get; set; } = [];
}
