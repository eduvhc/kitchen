using EmailService.Common;
using EmailService.RateLimiting;
using EmailService.Features.Emails.CancelEmail;
using EmailService.Features.Emails.GetEmail;
using EmailService.Features.Emails.ListEmails;
using EmailService.Features.Emails.SendEmail;

namespace EmailService.Features.Emails;

public static class EmailsFeature
{
    public static IServiceCollection AddEmails(this IServiceCollection services)
    {
        services.AddScoped<IEmailQueue, EmailQueue>();
        services.AddScoped<SendEmailHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapEmails(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/emails").WithTags("Emails").RequireRateLimiting(RateLimitingExtensions.PolicyName);

        group.MapEndpoint<SendEmailEndpoint>();
        group.MapEndpoint<GetEmailEndpoint>();
        group.MapEndpoint<ListEmailsEndpoint>();
        group.MapEndpoint<CancelEmailEndpoint>();

        return app;
    }
}
