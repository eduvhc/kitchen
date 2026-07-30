using EmailService.Templating.Abstractions;

namespace EmailService.Templating;

public static class TemplatingExtensions
{
    public static IServiceCollection AddScribanTemplating(this IServiceCollection services)
    {
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        return services;
    }
}
