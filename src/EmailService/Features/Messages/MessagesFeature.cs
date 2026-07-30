using EmailService.Common;
using EmailService.Features.Emails.SendEmail;
using EmailService.Features.Messages.GetMessage;
using EmailService.Features.Messages.ReceiveMessage;
using EmailService.RateLimiting;
using MessagingKit.Inbox;

namespace EmailService.Features.Messages;

public static class MessagesFeature
{
    public const string SendEmailMessageType = "send-email";

    public static IServiceCollection AddMessages(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddInbox<Persistence.EmailDbContext>(configuration)
            .AddHandler<SendEmailRequest, SendEmailMessageHandler>(SendEmailMessageType);

        return services;
    }

    public static IEndpointRouteBuilder MapMessages(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/messages")
            .WithTags("Messages")
            .RequireRateLimiting(RateLimitingExtensions.PolicyName);

        group.MapEndpoint<ReceiveMessageEndpoint>();
        group.MapEndpoint<GetMessageEndpoint>();

        return app;
    }
}
