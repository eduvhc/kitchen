using MailingKit.Diagnostics;
using MailingKit.Options;
using MailingKit.Templates;
using MailingKit.Templating;
using MailingKit.Transport;
using MessagingKit.Abstractions;
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
    /// <c>AddSmtpTransport()</c>, add <c>MailingKit.Resend</c> and call <c>AddResendTransport()</c>,
    /// or supply your own <c>IEmailSender</c>, before or after this call.
    /// <para>
    /// Every send is traced and measured under <see cref="MailingKitInstrumentation"/>; the host opts in
    /// by listening to <see cref="MailingKitInstrumentation.ActivitySourceName"/> and
    /// <see cref="MailingKitInstrumentation.MeterName"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">The host's <see cref="DbContext"/>, which holds the send log.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets <see cref="MailingOptions"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddMailing<TContext>(
        this IServiceCollection services,
        Action<MailingOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MailingOptions();
        configure?.Invoke(options);

        services.AddMetrics();
        services.TryAddSingleton<MailingKitMetrics>();
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
        InstrumentHandler<TContext>(services);

        return services;
    }

    /// <summary>
    /// Swaps the handler registration for a factory that hands it an instrumented sender. Wrapping here,
    /// rather than decorating <see cref="IEmailSender"/>, works whether the transport is registered before or
    /// after <c>AddMailing</c>, because the sender is only looked up when the handler is built.
    /// </summary>
    private static void InstrumentHandler<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];

            if (descriptor.ServiceType != typeof(IMessageHandler<SendEmail>)
                || descriptor.ImplementationType != typeof(SendEmailHandler<TContext>))
            {
                continue;
            }

            // ActivatorUtilities resolves every other constructor parameter from the container.
            services[i] = ServiceDescriptor.Describe(
                typeof(IMessageHandler<SendEmail>),
                sp => ActivatorUtilities.CreateInstance<SendEmailHandler<TContext>>(
                    sp,
                    InstrumentedEmailSender.Wrap(
                        sp.GetRequiredService<IEmailSender>(),
                        sp.GetRequiredService<MailingKitMetrics>())),
                descriptor.Lifetime);

            return;
        }
    }
}
