using EmailService.Features.Emails.SendEmail;
using MessagingKit;

namespace EmailService.Features.Messages;

public sealed class SendEmailMessageHandler(SendEmailHandler handler) : IMessageHandler<SendEmailRequest>
{
    public async Task HandleAsync(SendEmailRequest message, MessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        var request = message.IdempotencyKey is null
            ? message with { IdempotencyKey = context.MessageId.ToString() }
            : message;

        await handler.HandleAsync(request, source: "inbox", ct);
    }
}
