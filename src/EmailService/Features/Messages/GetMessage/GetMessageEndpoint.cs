using EmailService.Common;
using MessagingKit.Inbox.Abstractions;

namespace EmailService.Features.Messages.GetMessage;

public sealed class GetMessageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetMessage")
            .WithSummary("Read the processing state of a received message");

    private static async Task<IResult> HandleAsync(Guid id, IInbox inbox, CancellationToken ct)
    {
        var message = await inbox.FindAsync(id, ct);

        return message is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                message.Id,
                message.Type,
                Status = message.Status.ToString(),
                message.AttemptCount,
                message.ReceivedAt,
                message.ProcessedAt,
                message.LastError,
            });
    }
}
