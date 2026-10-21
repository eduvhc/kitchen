using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LockingKit.Azure;

/// <summary>Backs lock providers with Azure blob leases.</summary>
public static partial class AzureBlobLeaseLockingBuilderExtensions
{
    /// <summary>The provider name reported as <c>lockingkit.provider</c>.</summary>
    public const string ProviderName = "azure_blob";

    /// <summary>
    /// Backs the provider with blob leases, with options bound from <c>Locking:{name}</c> of
    /// <paramref name="configuration"/>. Options are validated at startup.
    /// </summary>
    /// <param name="builder">The builder from <c>AddLocking</c>.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The builder.</returns>
    public static LockingBuilder UseAzureBlobLeases(this LockingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddOptions<AzureBlobLeaseOptions>(builder.Name)
            .Bind(configuration.GetSection(DistributedLocks.SectionPath(builder.Name)));

        return UseCore(builder);
    }

    /// <summary>Backs the provider with blob leases, with options set in code. Options are validated at startup.</summary>
    /// <param name="builder">The builder from <c>AddLocking</c>.</param>
    /// <param name="configure">Sets <see cref="AzureBlobLeaseOptions"/>.</param>
    /// <returns>The builder.</returns>
    public static LockingBuilder UseAzureBlobLeases(this LockingBuilder builder, Action<AzureBlobLeaseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddOptions<AzureBlobLeaseOptions>(builder.Name).Configure(configure);
        return UseCore(builder);
    }

    internal static BlobContainerClient CreateContainerClient(AzureBlobLeaseOptions options)
    {
        var clientOptions = new BlobClientOptions();
        clientOptions.Retry.NetworkTimeout = options.RequestTimeout;
        clientOptions.Retry.MaxRetries = 2;
        clientOptions.Retry.Delay = TimeSpan.FromMilliseconds(100);
        clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(1);

        var service = options.ConnectionString is { Length: > 0 } connectionString
            ? new BlobServiceClient(connectionString, clientOptions)
            : options.Credential is { } credential
                ? new BlobServiceClient(options.ServiceUri, credential, clientOptions)
                : new BlobServiceClient(options.ServiceUri, clientOptions);

        return service.GetBlobContainerClient(options.ContainerName);
    }

    private static LockingBuilder UseCore(LockingBuilder builder)
    {
        var path = DistributedLocks.SectionPath(builder.Name);

        builder.Services.AddOptions<AzureBlobLeaseOptions>(builder.Name)
            .Validate(
                o => string.IsNullOrWhiteSpace(o.ConnectionString) != (o.ServiceUri is null),
                $"{path}: set exactly one of ConnectionString and ServiceUri.")
            .Validate(
                o => o.ServiceUri is null || (o.ServiceUri.IsAbsoluteUri && o.ServiceUri.Scheme is "https" or "http"),
                $"{path}:ServiceUri must be an absolute http(s) URL.")
            .Validate(
                o => o.ContainerName is not null && ContainerNamePattern().IsMatch(o.ContainerName),
                $"{path}:ContainerName is required: 3-63 lower-case letters, digits and single hyphens.")
            .Validate(
                o => BlobLockNames.IsValidPrefix(o.BlobPrefix),
                $"{path}:BlobPrefix must be empty or end with '/': letters, digits, '-', '_', '.' and '/', at most {BlobLockNames.MaxPrefixLength} characters.")
            .Validate(
                o => o.LeaseDuration >= TimeSpan.FromSeconds(15) && o.LeaseDuration <= TimeSpan.FromSeconds(60),
                $"{path}:LeaseDuration must be between 15 and 60 seconds; Azure accepts no other finite lease.")
            .Validate(o => o.RequestTimeout > TimeSpan.Zero, $"{path}:RequestTimeout must be greater than zero.");

        return builder.UseBackend<AzureBlobLeaseOptions>(
            ProviderName,
            (_, o) => new AzureBlobLeaseBackend(CreateContainerClient(o), o.BlobPrefix, o.LeaseDuration, o.FencingTokens));
    }

    [GeneratedRegex("^(?=.{3,63}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ContainerNamePattern();
}
