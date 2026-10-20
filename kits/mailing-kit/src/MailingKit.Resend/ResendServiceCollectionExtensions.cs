using System.Net.Http.Headers;
using MailingKit.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MailingKit.Resend;

/// <summary>Registers <see cref="ResendEmailSender"/> as the host's <see cref="IEmailSender"/>.</summary>
public static class ResendServiceCollectionExtensions
{
    private static readonly string ProductVersion =
        typeof(ResendEmailSender).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Registers the Resend transport with options bound from the <c>Resend</c> section of
    /// <paramref name="configuration"/>. Options are validated at startup: a missing <c>ApiKey</c> fails the host.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root; the <see cref="ResendOptions.SectionName"/> section is bound.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddResendTransport(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ResendOptions>().Bind(configuration.GetSection(ResendOptions.SectionName));
        return AddCore(services);
    }

    /// <summary>
    /// Registers the Resend transport with options set in code. Options are validated at startup: a missing
    /// <c>ApiKey</c> fails the host.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets <see cref="ResendOptions"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddResendTransport(this IServiceCollection services, Action<ResendOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ResendOptions>().Configure(configure);
        return AddCore(services);
    }

    private static IServiceCollection AddCore(IServiceCollection services)
    {
        services.AddOptions<ResendOptions>()
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Resend:ApiKey is required.")
            .Validate(
                o => o.BaseUrl is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp),
                "Resend:BaseUrl must be an absolute http(s) URL.")
            .Validate(o => o.TimeoutSeconds > 0, "Resend:TimeoutSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddHttpClient<ResendEmailSender>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<ResendOptions>>().Value;

            // Without the trailing slash, the relative "emails" path would replace the last segment.
            var root = options.BaseUrl.AbsoluteUri;
            http.BaseAddress = new Uri(root.EndsWith('/') ? root : root + "/");
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

            // Resend rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MailingKit.Resend", ProductVersion));
        });

        services.TryAddTransient<IEmailSender>(sp => sp.GetRequiredService<ResendEmailSender>());

        return services;
    }
}
