namespace MailingKit.Templates;

public class EmailTemplate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Key { get; set; }
    public string? Description { get; set; }

    public required string SubjectTemplate { get; set; }
    public string? HtmlTemplate { get; set; }
    public string? TextTemplate { get; set; }

    public string? FromAddress { get; set; }
    public string? FromName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
