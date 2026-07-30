namespace EmailService.Features.Templates.PreviewTemplate;

public record PreviewTemplateResponse(string Subject, string? Html, string? Text);
