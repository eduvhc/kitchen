namespace EmailService.Features.Dispatch;

public static class DispatchFeature
{
    public static IServiceCollection AddDispatch(this IServiceCollection services)
    {
        services.AddSingleton<EmailDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<EmailDispatcher>());
        return services;
    }
}
