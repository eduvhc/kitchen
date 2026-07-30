using EmailService.Features.Templates.Domain;

namespace EmailService.Features.Templates.Contracts;

public record TemplateResponse(
    string Key,
    string? Description,
    string Subject,
    string? Html,
    string? Text,
    string? From,
    string? FromName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TemplateResponse FromEntity(EmailTemplate t) => new(
        t.Key,
        t.Description,
        t.SubjectTemplate,
        t.HtmlTemplate,
        t.TextTemplate,
        t.FromAddress,
        t.FromName,
        t.IsActive,
        t.CreatedAt,
        t.UpdatedAt);
}
