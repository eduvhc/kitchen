using EmailService.Common;

namespace EmailService.Features.Templates.DeleteTemplate;

public sealed class DeleteTemplateEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{key}", HandleAsync)
            .WithName("DeleteTemplate")
            .WithSummary("Delete a template");

    private static async Task<IResult> HandleAsync(string key, ITemplateStore store, CancellationToken ct)
    {
        var deleted = await store.DeleteAsync(key, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
