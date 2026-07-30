using EmailService.Common;
using EmailService.Templating;

namespace EmailService.Features.Templates.PreviewTemplate;

public record PreviewTemplateRequest
{
    public Dictionary<string, object?> Model { get; init; } = [];
}

public record PreviewTemplateResponse(string Subject, string? Html, string? Text);

public sealed class PreviewTemplateEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{key}/preview", HandleAsync)
            .WithName("PreviewTemplate")
            .WithSummary("Render a template against a model without sending");

    private static async Task<IResult> HandleAsync(
        string key,
        PreviewTemplateRequest request,
        ITemplateStore store,
        ITemplateRenderer renderer,
        CancellationToken ct)
    {
        var template = await store.GetByKeyAsync(key, ct);
        if (template is null)
        {
            return Results.NotFound();
        }

        try
        {
            return Results.Ok(new PreviewTemplateResponse(
                renderer.Render(template.SubjectTemplate, request.Model),
                template.HtmlTemplate is null ? null : renderer.Render(template.HtmlTemplate, request.Model),
                template.TextTemplate is null ? null : renderer.Render(template.TextTemplate, request.Model)));
        }
        catch (TemplateRenderException ex)
        {
            return Results.Problem(
                title: "Template render failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }
}
