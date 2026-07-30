using Microsoft.AspNetCore.Authorization;

namespace EmailService.Authentication;

public static class AuthenticationExtensions
{
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<ApiKeySchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                _ => { });

        services
            .AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "admin"));

        return services;
    }
}
