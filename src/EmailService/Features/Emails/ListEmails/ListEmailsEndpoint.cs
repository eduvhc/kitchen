using EmailService.Common;
using Microsoft.AspNetCore.Mvc;

namespace EmailService.Features.Emails.ListEmails;

public sealed class ListEmailsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", HandleAsync)
            .WithName("ListEmails")
            .WithSummary("List emails, newest first");

    private static async Task<IResult> HandleAsync(
        IEmailQueue queue,
        [FromQuery] EmailStatus? status,
        [FromQuery] string? recipient,
        [FromQuery] string? template,
        [FromQuery] string? source,
        [FromQuery] int limit,
        [FromQuery] int offset,
        CancellationToken ct)
    {
        var filter = new EmailQueryFilter(
            status,
            recipient,
            template,
            source,
            limit <= 0 ? 50 : limit,
            Math.Max(0, offset));

        var results = await queue.ListAsync(filter, ct);
        return Results.Ok(results.Select(EmailResponse.FromEntity));
    }
}
