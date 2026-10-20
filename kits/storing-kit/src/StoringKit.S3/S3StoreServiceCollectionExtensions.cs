using Amazon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace StoringKit.S3;

/// <summary>Registers S3-compatible buckets as <see cref="IObjectStore"/>s.</summary>
public static class S3StoreServiceCollectionExtensions
{
    /// <summary>The provider name reported as <c>storingkit.provider</c>.</summary>
    public const string ProviderName = "s3";

    /// <summary>
    /// Registers a bucket with options bound from <c>Storing:{name}</c> of <paramref name="configuration"/>.
    /// Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="name">The store name; <see cref="ObjectStores.DefaultName"/> is also resolvable unkeyed.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddS3Store(
        this IServiceCollection services,
        IConfiguration configuration,
        string name = ObjectStores.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.AddOptions<S3StoreOptions>(name).Bind(configuration.GetSection(ObjectStores.SectionPath(name)));
        return AddCore(services, name);
    }

    /// <summary>Registers the default bucket with options set in code. Options are validated at startup.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets <see cref="S3StoreOptions"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddS3Store(this IServiceCollection services, Action<S3StoreOptions> configure) =>
        services.AddS3Store(ObjectStores.DefaultName, configure);

    /// <summary>Registers a named bucket with options set in code. Options are validated at startup.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The store name; <see cref="ObjectStores.DefaultName"/> is also resolvable unkeyed.</param>
    /// <param name="configure">Sets <see cref="S3StoreOptions"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddS3Store(this IServiceCollection services, string name, Action<S3StoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<S3StoreOptions>(name).Configure(configure);
        return AddCore(services, name);
    }

    private static IServiceCollection AddCore(IServiceCollection services, string name)
    {
        var path = ObjectStores.SectionPath(name);

        services.AddOptions<S3StoreOptions>(name)
            .Validate(o => !string.IsNullOrWhiteSpace(o.BucketName), $"{path}:BucketName is required.")
            .Validate(
                o => o.ServiceUrl is not null || !string.IsNullOrWhiteSpace(o.Region),
                $"{path}: set Region for AWS, or ServiceUrl for another S3-compatible store.")
            .Validate(
                o => o.ServiceUrl is not null || o.Region is null || RegionEndpoint.EnumerableAllRegions.Any(r => r.SystemName == o.Region),
                $"{path}:Region is not a known AWS region.")
            .Validate(
                o => o.ServiceUrl is null || (o.ServiceUrl.IsAbsoluteUri && o.ServiceUrl.Scheme is "https" or "http"),
                $"{path}:ServiceUrl must be an absolute http(s) URL.")
            .Validate(
                o => string.IsNullOrEmpty(o.AccessKeyId) == string.IsNullOrEmpty(o.SecretAccessKey),
                $"{path}: set both AccessKeyId and SecretAccessKey, or neither.")
            .Validate(o => o.HasValidPublicBaseUrl(), $"{path}:PublicBaseUrl must be an absolute http(s) URL.")
            .ValidateOnStart();

        return services.AddObjectStore(name, ProviderName, sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<S3StoreOptions>>().Get(name);
            return new S3ObjectStore(S3ObjectStore.CreateClient(options), options, sp.GetRequiredService<TimeProvider>());
        });
    }
}
