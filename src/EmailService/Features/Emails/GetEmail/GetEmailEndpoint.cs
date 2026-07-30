using EmailService.Common;
using EmailService.Features.Emails.Abstractions;
using EmailService.Features.Emails.Contracts;

namespace EmailService.Features.Emails.GetEmail;

public sealed class GetEmailEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetEmail")
            .WithSummary("Read one email and its delivery state");

    private static async Task<IResult> HandleAsync(Guid id, IEmailQueue queue, CancellationToken ct)
    {
        var message = await queue.FindAsync(id, ct);
        return message is null ? Results.NotFound() : Results.Ok(EmailResponse.FromEntity(message));
    }
}
