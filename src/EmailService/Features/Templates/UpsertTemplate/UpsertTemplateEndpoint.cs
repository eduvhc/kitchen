using EmailService.Common;
using EmailService.Features.Templates.Abstractions;
using EmailService.Features.Templates.Contracts;
using EmailService.Features.Templates.Domain;
using EmailService.Templating.Abstractions;

namespace EmailService.Features.Templates.UpsertTemplate;

public sealed class UpsertTemplateEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/{key}", HandleAsync)
            .WithName("UpsertTemplate")
            .WithSummary("Create or replace a template");

    private static async Task<IResult> HandleAsync(
        string key,
        UpsertTemplateRequest request,
        ITemplateStore store,
        ITemplateRenderer renderer,
        CancellationToken ct)
    {
        try
        {
            var empty = new Dictionary<string, object?>();
            renderer.Render(request.Subject, empty);

            if (request.Html is not null)
            {
                renderer.Render(request.Html, empty);
            }

            if (request.Text is not null)
            {
                renderer.Render(request.Text, empty);
            }
        }
        catch (TemplateRenderException ex)
        {
            return Results.Problem(
                title: "Invalid template",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var saved = await store.UpsertAsync(
            new EmailTemplate
            {
                Key = key,
                Description = request.Description,
                SubjectTemplate = request.Subject,
                HtmlTemplate = request.Html,
                TextTemplate = request.Text,
                FromAddress = request.From,
                FromName = request.FromName,
                IsActive = request.IsActive,
            },
            ct);

        return Results.Ok(TemplateResponse.FromEntity(saved));
    }
}
