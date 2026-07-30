using EmailService.Common;

namespace EmailService.Features.Emails.CancelEmail;

public sealed class CancelEmailEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/cancel", HandleAsync)
            .WithName("CancelEmail")
            .WithSummary("Cancel an email that has not left the queue");

    private static async Task<IResult> HandleAsync(Guid id, IEmailQueue queue, CancellationToken ct)
    {
        var cancelled = await queue.CancelAsync(id, ct);

        return cancelled
            ? Results.NoContent()
            : Results.Problem(
                title: "Cannot cancel",
                detail: "The email does not exist or has already left the queue.",
                statusCode: StatusCodes.Status409Conflict);
    }
}
