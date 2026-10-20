using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Testcontainers.Minio;

namespace TestingKit.Minio;

public sealed class MinioContainerOptions : ContainerOptions
{
    // MinIO stopped publishing images to Docker Hub and Quay in 2025; Chainguard builds it from source.
    // Its free tier serves only :latest, so pin a digest here if you need a frozen build.
    public const string DefaultImage = "cgr.dev/chainguard/minio:latest";

    public MinioContainerOptions() => Image = DefaultImage;

    public string Username { get; set; } = "minioadmin";

    public string Password { get; set; } = "minioadmin";
}

public sealed class MinioClientOptions : ClientOptions
{
    /// <summary>Access key for an external server (<see cref="ClientOptions.ConnectionString"/> is its endpoint URL).</summary>
    public string? AccessKey { get; set; }

    /// <summary>Secret key for an external server.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Buckets created on start, if missing.</summary>
    public IList<string> Buckets { get; } = [];

    /// <summary>Buckets emptied by <see cref="MinioFixture.ResetAsync"/>.</summary>
    public IList<string> BucketsToReset { get; } = [];
}

public class MinioFixture(
    MinioContainerOptions? containerOptions = null,
    MinioClientOptions? clientOptions = null)
    : TestFixtureBase<MinioContainerOptions, MinioClientOptions>(containerOptions, clientOptions), IResettableFixture, IDisposable
{
    private MinioContainer? _container;
    private AmazonS3Client? _s3;

    /// <summary>The S3 endpoint, <c>http://host:port/</c>. Use path-style addressing against it.</summary>
    public Uri Endpoint { get; private set; } = null!;

    public string AccessKey { get; private set; } = null!;

    public string SecretKey { get; private set; } = null!;

    /// <summary>A path-style client for the server, signed for <c>us-east-1</c>.</summary>
    public AmazonS3Client S3Client => _s3 ?? throw new InvalidOperationException(
        $"{nameof(MinioFixture)} is not ready. Call StartAsync() first.");

    public async Task CreateBucketAsync(string bucket, CancellationToken ct = default)
    {
        EnsureReady();

        try
        {
            await S3Client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    /// <summary>Lets anyone read objects in <paramref name="bucket"/> without signing, as a public bucket would.</summary>
    public Task AllowPublicReadAsync(string bucket, CancellationToken ct = default)
    {
        EnsureReady();

        var policy = $$"""
            {
              "Version": "2012-10-17",
              "Statement": [{
                "Effect": "Allow",
                "Principal": { "AWS": ["*"] },
                "Action": ["s3:GetObject"],
                "Resource": ["arn:aws:s3:::{{bucket}}/*"]
              }]
            }
            """;

        return S3Client.PutBucketPolicyAsync(new PutBucketPolicyRequest { BucketName = bucket, Policy = policy }, ct);
    }

    public async Task UploadAsync(string bucket, string key, string content, CancellationToken ct = default)
    {
        EnsureReady();
        await CreateBucketAsync(bucket, ct);
        await S3Client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, ContentBody = content }, ct);
    }

    public async Task<string?> DownloadAsync(string bucket, string key, CancellationToken ct = default)
    {
        EnsureReady();

        try
        {
            using var response = await S3Client.GetObjectAsync(bucket, key, ct);
            using var reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ListObjectsAsync(string bucket, string? prefix = null, CancellationToken ct = default)
    {
        EnsureReady();
        var keys = new List<string>();
        string? token = null;

        do
        {
            ListObjectsV2Response page;

            try
            {
                page = await S3Client.ListObjectsV2Async(
                    new ListObjectsV2Request { BucketName = bucket, Prefix = prefix, ContinuationToken = token },
                    ct);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                return [];
            }

            keys.AddRange((page.S3Objects ?? []).Select(o => o.Key));
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (token is not null);

        return keys;
    }

    public async Task ResetBucketAsync(string bucket, CancellationToken ct = default)
    {
        foreach (var key in await ListObjectsAsync(bucket, ct: ct))
        {
            await S3Client.DeleteObjectAsync(bucket, key, ct);
        }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        foreach (var bucket in Client.BucketsToReset)
        {
            await ResetBucketAsync(bucket, ct);
        }
    }

    public void Dispose()
    {
        _s3?.Dispose();
        _s3 = null;
        GC.SuppressFinalize(this);
    }

    protected override async Task StartContainerAsync(CancellationToken ct)
    {
        var builder = new MinioBuilder(Container.Image!)
            .WithUsername(Container.Username)
            .WithPassword(Container.Password)
            .WithReuse(Container.Reuse);

        foreach (var (key, value) in Container.Labels)
        {
            builder = builder.WithLabel(key, value);
        }

        _container = builder.Build();
        await _container.StartAsync(ct);

        ConnectionString = _container.GetConnectionString();
        AccessKey = _container.GetAccessKey();
        SecretKey = _container.GetSecretKey();
    }

    protected override async ValueTask DisposeContainerAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override Task OnExternalConnectedAsync(CancellationToken ct)
    {
        AccessKey = Client.AccessKey ?? throw new InvalidOperationException(
            $"{nameof(MinioClientOptions)}.{nameof(MinioClientOptions.AccessKey)} is required when using an external server.");
        SecretKey = Client.SecretKey ?? throw new InvalidOperationException(
            $"{nameof(MinioClientOptions)}.{nameof(MinioClientOptions.SecretKey)} is required when using an external server.");

        return Task.CompletedTask;
    }

    protected override async Task OnAfterStartAsync(CancellationToken ct)
    {
        var root = ConnectionString.EndsWith('/') ? ConnectionString : ConnectionString + "/";
        Endpoint = new Uri(root);

        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config
            {
                ServiceURL = Endpoint.AbsoluteUri,
                AuthenticationRegion = "us-east-1",
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });

        foreach (var bucket in Client.Buckets)
        {
            await CreateBucketAsync(bucket, ct);
        }
    }

    protected override Task OnBeforeDisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}
