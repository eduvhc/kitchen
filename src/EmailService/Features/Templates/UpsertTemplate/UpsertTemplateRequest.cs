namespace EmailService.Features.Templates.UpsertTemplate;

public record UpsertTemplateRequest
{
    public string? Description { get; init; }
    public required string Subject { get; init; }
    public string? Html { get; init; }
    public string? Text { get; init; }
    public string? From { get; init; }
    public string? FromName { get; init; }
    public bool IsActive { get; init; } = true;
}
