using EmailService.Transport.Abstractions;
using EmailService.Transport.Smtp;

namespace EmailService.Transport;

public static class TransportExtensions
{
    public static IServiceCollection AddSmtpTransport(this IServiceCollection services)
    {
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
