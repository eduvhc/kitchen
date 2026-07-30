namespace EmailService.Options;

public static class OptionsExtensions
{
    public static IServiceCollection AddServiceOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<DispatcherOptions>(configuration.GetSection(DispatcherOptions.SectionName));
        services.Configure<EmailDefaultsOptions>(configuration.GetSection(EmailDefaultsOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        return services;
    }
}
