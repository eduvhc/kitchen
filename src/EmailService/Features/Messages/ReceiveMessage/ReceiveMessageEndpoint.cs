using EmailService.Common;
using MessagingKit;
using MessagingKit.Inbox.Abstractions;

namespace EmailService.Features.Messages.ReceiveMessage;

public sealed class ReceiveMessageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", HandleAsync)
            .WithName("ReceiveMessage")
            .WithSummary("Accept a message into the inbox for once-only processing");

    private static async Task<IResult> HandleAsync(
        MessageEnvelope envelope,
        IInbox inbox,
        CancellationToken ct)
    {
        if (envelope.Id == Guid.Empty)
        {
            return Results.Problem(
                title: "Invalid message",
                detail: "An envelope requires a non-empty id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var stored = await inbox.TryStoreAsync(envelope, ct);

        return stored
            ? Results.Accepted($"/v1/messages/{envelope.Id}")
            : Results.Ok(new { envelope.Id, duplicate = true });
    }
}
