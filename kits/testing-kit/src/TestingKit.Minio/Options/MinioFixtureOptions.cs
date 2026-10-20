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
