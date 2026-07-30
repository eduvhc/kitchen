using EmailService.Common;

namespace EmailService.Features.Templates.GetTemplate;

public sealed class GetTemplateEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{key}", HandleAsync)
            .WithName("GetTemplate")
            .WithSummary("Read one template");

    private static async Task<IResult> HandleAsync(string key, ITemplateStore store, CancellationToken ct)
    {
        var template = await store.GetByKeyAsync(key, ct);
        return template is null ? Results.NotFound() : Results.Ok(TemplateResponse.FromEntity(template));
    }
}
