using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoringKit.Azure;
using StoringKit.S3;
using TestingKit;
using TestingKit.Azurite;
using TestingKit.Minio;

namespace StoringKit.IntegrationTests;

/// <summary>
/// One host per provider, each with a private default store and a public one, wired the way an application would
/// wire them. Both run the same <see cref="ObjectStoreContract"/>.
/// </summary>
[TestClass]
public static class TestHost
{
    public const string PrivateBucket = "private";
    public const string PublicBucket = "public";
    public const string PublicStore = "Public";

    public static TestEnvironment Environment { get; } = new();

    public static AzuriteFixture Azurite { get; } = new();

    public static MinioFixture Minio { get; } = new(clientOptions: new MinioClientOptions { Buckets = { PrivateBucket, PublicBucket } });

    public static ServiceProvider AzureServices { get; private set; } = null!;

    public static ServiceProvider S3Services { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        Environment.AddFixture(Azurite);
        Environment.AddFixture(Minio);

        await Environment.StartAsync(context.CancellationToken);

        await Azurite.BlobClient.GetBlobContainerClient(PrivateBucket)
            .CreateIfNotExistsAsync(cancellationToken: context.CancellationToken);
        await Azurite.BlobClient.GetBlobContainerClient(PublicBucket)
            .CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: context.CancellationToken);
        await Minio.AllowPublicReadAsync(PublicBucket, context.CancellationToken);

        var azure = new ServiceCollection();
        azure.AddAzureBlobStore(o =>
        {
            o.ConnectionString = Azurite.ConnectionString;
            o.ContainerName = PrivateBucket;
        });
        azure.AddAzureBlobStore(PublicStore, o =>
        {
            o.ConnectionString = Azurite.ConnectionString;
            o.ContainerName = PublicBucket;
            o.PublicBaseUrl = new Uri($"{Azurite.BlobClient.Uri.AbsoluteUri.TrimEnd('/')}/{PublicBucket}/");
        });
        AzureServices = Build(azure);

        var s3 = new ServiceCollection();
        s3.AddS3Store(o => ConfigureMinio(o, PrivateBucket));
        s3.AddS3Store(PublicStore, o =>
        {
            ConfigureMinio(o, PublicBucket);
            o.PublicBaseUrl = new Uri(Minio.Endpoint, $"{PublicBucket}/");
        });
        S3Services = Build(s3);
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        await AzureServices.DisposeAsync();
        await S3Services.DisposeAsync();
        await Environment.DisposeAsync();
    }

    private static void ConfigureMinio(S3StoreOptions options, string bucket)
    {
        options.BucketName = bucket;
        options.ServiceUrl = Minio.Endpoint;
        options.ForcePathStyle = true;
        options.AccessKeyId = Minio.AccessKey;
        options.SecretAccessKey = Minio.SecretKey;
    }

    private static ServiceProvider Build(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        return provider;
    }
}
