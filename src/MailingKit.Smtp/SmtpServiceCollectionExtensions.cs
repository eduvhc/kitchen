using MailingKit.Transport;


namespace MailingKit.Smtp;

public static class TransportExtensions
{
    public static IServiceCollection AddSmtpTransport(this IServiceCollection services)
    {
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
