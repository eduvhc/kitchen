using EmailService.Features.Emails.SendEmail;
using EmailService.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Threading.RateLimiting;

namespace EmailService.RateLimiting;

public static class RateLimitingExtensions
{
    public const string PolicyName = "per-source";

    private const int MaxPartitionKeyLength = 64;

    public static IServiceCollection AddSourceRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
                        title = "Too many requests",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = $"Rate limit exceeded for source '{PartitionKeyFor(context.HttpContext)}'.",
                    },
                    ct);
            };

            limiter.AddPolicy(PolicyName, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                var key = PartitionKeyFor(context);

                if (!options.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter(key);
                }

                var overrides = options.Sources.GetValueOrDefault(key);

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = overrides?.PermitLimit ?? options.PermitLimit,
                    Window = TimeSpan.FromSeconds(overrides?.WindowSeconds ?? options.WindowSeconds),
                    QueueLimit = overrides?.QueueLimit ?? options.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });
        });

        return services;
    }

    private static string PartitionKeyFor(HttpContext context)
    {
        var source = context.Request.Headers[SendEmailEndpoint.SourceHeader].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(source))
        {
            var trimmed = source.Trim().ToLowerInvariant();
            return trimmed.Length > MaxPartitionKeyLength ? trimmed[..MaxPartitionKeyLength] : trimmed;
        }

        return $"ip:{context.Connection.RemoteIpAddress}";
    }
}
