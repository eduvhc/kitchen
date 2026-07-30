namespace EmailService.Features.Templates.PreviewTemplate;

public record PreviewTemplateRequest
{
    public Dictionary<string, object?> Model { get; init; } = [];
}
