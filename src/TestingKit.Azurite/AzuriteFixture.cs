using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Testcontainers.Azurite;

namespace TestingKit.Azurite;

public sealed class AzuriteContainerOptions : ContainerOptions
{
    public const string DefaultImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    public AzuriteContainerOptions() => Image = DefaultImage;

    public bool SkipApiVersionCheck { get; set; } = true;
}

public sealed class AzuriteClientOptions : ClientOptions
{
    public IList<string> ContainersToReset { get; } = [];
}

public class AzuriteFixture(
    AzuriteContainerOptions? containerOptions = null,
    AzuriteClientOptions? clientOptions = null)
    : TestFixtureBase<AzuriteContainerOptions, AzuriteClientOptions>(containerOptions, clientOptions), IResettableFixture
{
    private AzuriteContainer? _container;

    public BlobServiceClient BlobClient { get; private set; } = null!;

    public Task UploadAsync(string containerName, string blobPath, string content, CancellationToken ct = default) =>
        UploadAsync(containerName, blobPath, new BinaryData(content), ct);

    public Task UploadAsync(string containerName, string blobPath, byte[] content, CancellationToken ct = default) =>
        UploadAsync(containerName, blobPath, new BinaryData(content), ct);

    public async Task UploadAsync(string containerName, string blobPath, BinaryData content, CancellationToken ct = default)
    {
        EnsureReady();
        var container = BlobClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        await container.GetBlobClient(blobPath).UploadAsync(content, overwrite: true, cancellationToken: ct);
    }

    public async Task<string?> DownloadAsync(string containerName, string blobPath, CancellationToken ct = default)
    {
        EnsureReady();
        var blob = BlobClient.GetBlobContainerClient(containerName).GetBlobClient(blobPath);

        if (!await blob.ExistsAsync(ct))
        {
            return null;
        }

        var content = await blob.DownloadContentAsync(ct);
        return content.Value.Content.ToString();
    }

    public async Task<IReadOnlyList<string>> ListBlobsAsync(string containerName, string? prefix = null, CancellationToken ct = default)
    {
        EnsureReady();
        var container = BlobClient.GetBlobContainerClient(containerName);

        if (!await container.ExistsAsync(ct))
        {
            return [];
        }

        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
        {
            names.Add(blob.Name);
        }

        return names;
    }

    public async Task ResetContainerAsync(string containerName, CancellationToken ct = default)
    {
        EnsureReady();
        var container = BlobClient.GetBlobContainerClient(containerName);

        if (!await container.ExistsAsync(ct))
        {
            return;
        }

        await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
        {
            await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: ct);
        }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        foreach (var container in Client.ContainersToReset)
        {
            await ResetContainerAsync(container, ct);
        }
    }

    protected override async Task StartContainerAsync(CancellationToken ct)
    {
        var builder = new AzuriteBuilder(Container.Image!)
            .WithReuse(Container.Reuse);

        if (Container.SkipApiVersionCheck)
        {
            builder = builder.WithCommand("--skipApiVersionCheck");
        }

        foreach (var (key, value) in Container.Labels)
        {
            builder = builder.WithLabel(key, value);
        }

        _container = builder.Build();
        await _container.StartAsync(ct);
        ConnectionString = _container.GetConnectionString();
    }

    protected override async ValueTask DisposeContainerAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override Task OnAfterStartAsync(CancellationToken ct)
    {
        BlobClient = new BlobServiceClient(ConnectionString);
        return Task.CompletedTask;
    }

    protected override Task OnExternalConnectedAsync(CancellationToken ct)
    {
        BlobClient = new BlobServiceClient(ConnectionString);
        return Task.CompletedTask;
    }
}
