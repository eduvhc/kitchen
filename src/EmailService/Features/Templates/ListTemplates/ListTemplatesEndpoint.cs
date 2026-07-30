using EmailService.Common;

namespace EmailService.Features.Templates.ListTemplates;

public sealed class ListTemplatesEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", HandleAsync)
            .WithName("ListTemplates")
            .WithSummary("List every template");

    private static async Task<IResult> HandleAsync(ITemplateStore store, CancellationToken ct)
    {
        var templates = await store.ListAsync(ct);
        return Results.Ok(templates.Select(TemplateResponse.FromEntity));
    }
}
