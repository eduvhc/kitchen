using EmailService.Common;
using EmailService.Features.Templates.Abstractions;
using EmailService.Features.Templates.DeleteTemplate;
using EmailService.Features.Templates.GetTemplate;
using EmailService.Features.Templates.ListTemplates;
using EmailService.Features.Templates.PreviewTemplate;
using EmailService.Features.Templates.UpsertTemplate;
using EmailService.RateLimiting;

namespace EmailService.Features.Templates;

public static class TemplatesFeature
{
    public static IServiceCollection AddTemplates(this IServiceCollection services)
    {
        services.AddScoped<ITemplateStore, TemplateStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapTemplates(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/templates").WithTags("Templates").RequireRateLimiting(RateLimitingExtensions.PolicyName);

        group.MapEndpoint<ListTemplatesEndpoint>();
        group.MapEndpoint<GetTemplateEndpoint>();
        group.MapEndpoint<UpsertTemplateEndpoint>();
        group.MapEndpoint<DeleteTemplateEndpoint>();
        group.MapEndpoint<PreviewTemplateEndpoint>();

        return app;
    }
}
