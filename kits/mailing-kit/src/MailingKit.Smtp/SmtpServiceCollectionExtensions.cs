using MailingKit.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MailingKit.Smtp;

public static class SmtpServiceCollectionExtensions
{
    public static IServiceCollection AddSmtpTransport(
        this IServiceCollection services,
        Action<SmtpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SmtpOptions();
        configure?.Invoke(options);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.TryAddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
