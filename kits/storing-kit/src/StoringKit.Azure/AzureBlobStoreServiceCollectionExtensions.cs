using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace StoringKit.Azure;

/// <summary>Registers Azure Blob Storage containers as <see cref="IObjectStore"/>s.</summary>
public static partial class AzureBlobStoreServiceCollectionExtensions
{
    /// <summary>The provider name reported as <c>storingkit.provider</c>.</summary>
    public const string ProviderName = "azure_blob";

    /// <summary>
    /// Registers a container with options bound from <c>Storing:{name}</c> of <paramref name="configuration"/>.
    /// Options are validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="name">The store name; <see cref="ObjectStores.DefaultName"/> is also resolvable unkeyed.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAzureBlobStore(
        this IServiceCollection services,
        IConfiguration configuration,
        string name = ObjectStores.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.AddOptions<AzureBlobStoreOptions>(name).Bind(configuration.GetSection(ObjectStores.SectionPath(name)));
        return AddCore(services, name);
    }

    /// <summary>Registers the default container with options set in code. Options are validated at startup.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets <see cref="AzureBlobStoreOptions"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAzureBlobStore(this IServiceCollection services, Action<AzureBlobStoreOptions> configure) =>
        services.AddAzureBlobStore(ObjectStores.DefaultName, configure);

    /// <summary>Registers a named container with options set in code. Options are validated at startup.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The store name; <see cref="ObjectStores.DefaultName"/> is also resolvable unkeyed.</param>
    /// <param name="configure">Sets <see cref="AzureBlobStoreOptions"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAzureBlobStore(
        this IServiceCollection services,
        string name,
        Action<AzureBlobStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<AzureBlobStoreOptions>(name).Configure(configure);
        return AddCore(services, name);
    }

    private static IServiceCollection AddCore(IServiceCollection services, string name)
    {
        var path = ObjectStores.SectionPath(name);

        services.AddOptions<AzureBlobStoreOptions>(name)
            .Validate(
                o => string.IsNullOrWhiteSpace(o.ConnectionString) != (o.ServiceUri is null),
                $"{path}: set exactly one of ConnectionString and ServiceUri.")
            .Validate(
                o => o.ServiceUri is null || (o.ServiceUri.IsAbsoluteUri && o.ServiceUri.Scheme is "https" or "http"),
                $"{path}:ServiceUri must be an absolute http(s) URL.")
            .Validate(
                o => o.ContainerName is not null && ContainerNamePattern().IsMatch(o.ContainerName),
                $"{path}:ContainerName is required: 3-63 lower-case letters, digits and single hyphens.")
            .Validate(o => o.HasValidPublicBaseUrl(), $"{path}:PublicBaseUrl must be an absolute http(s) URL.")
            .ValidateOnStart();

        return services.AddObjectStore(name, ProviderName, sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<AzureBlobStoreOptions>>().Get(name);

            var service = options.ConnectionString is { Length: > 0 } connectionString
                ? new BlobServiceClient(connectionString)
                : options.Credential is { } credential
                    ? new BlobServiceClient(options.ServiceUri, credential)
                    : new BlobServiceClient(options.ServiceUri);

            return new AzureBlobObjectStore(
                service,
                options.ContainerName!,
                canUseDelegationKey: options.Credential is not null && options.ConnectionString is not { Length: > 0 },
                options.PublicBaseUrl,
                sp.GetRequiredService<TimeProvider>());
        });
    }

    [GeneratedRegex("^(?=.{3,63}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ContainerNamePattern();
}
