using MailingKit.Options;
using MailingKit.Templates;
using MailingKit.Templating;
using MessagingKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MailingKit;

public static class MailingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email module against the host's <typeparamref name="TContext"/>: the
    /// <c>send-email</c> handler, template rendering, and the send log.
    /// </summary>
    /// <remarks>
    /// A transport still has to be registered — add <c>MailingKit.Smtp</c> and call
    /// <c>AddSmtpTransport()</c>, or supply your own <c>IEmailSender</c>.
    /// </remarks>
    public static IServiceCollection AddMailing<TContext>(
        this IServiceCollection services,
        Action<MailingOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MailingOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.TryAddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();

        switch (options.Templates.Storage)
        {
            case TemplateStorage.Files:
                services.TryAddScoped<ITemplateStore, FileTemplateStore>();
                break;

            case TemplateStorage.Database:
                services.TryAddScoped<ITemplateStore, DbTemplateStore<TContext>>();
                services.TryAddScoped<IWritableTemplateStore, DbTemplateStore<TContext>>();
                break;

            case TemplateStorage.None:
            default:
                // No store: messages naming a template fail with an explanatory error.
                break;
        }

        // The handler is what MessagingKit resolves when a send-email message arrives.
        services.AddMessageHandler<SendEmail, SendEmailHandler<TContext>>();

        return services;
    }
}
