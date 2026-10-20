using Amazon.Runtime;

namespace StoringKit.S3;

/// <summary>
/// Settings for one S3-compatible bucket: AWS S3, Cloudflare R2, MinIO or another store that speaks the S3 API.
/// Bound from <c>Storing:{name}</c> by <c>AddS3Store(configuration, name)</c>.
/// </summary>
/// <remarks>
/// For AWS, set <see cref="Region"/> and leave <see cref="ServiceUrl"/> unset. For anything else set
/// <see cref="ServiceUrl"/>: StoringKit then sends checksums only when an operation requires them, because recent AWS
/// SDKs add CRC checksums by default and R2 and other S3-compatible stores reject them.
/// </remarks>
public sealed class S3StoreOptions : ObjectStoreOptions
{
    /// <summary>The bucket. Required; it must already exist — StoringKit does not create or reconfigure it.</summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// The AWS region, such as <c>eu-west-1</c>. Required without <see cref="ServiceUrl"/>; with it, the region requests
    /// are signed for — <c>auto</c> for R2, <c>us-east-1</c> (the default) for MinIO.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// The endpoint of a non-AWS store: <c>https://{account}.r2.cloudflarestorage.com</c>, <c>http://localhost:9000</c>.
    /// Leave unset for AWS S3.
    /// </summary>
    public Uri? ServiceUrl { get; set; }

    /// <summary>
    /// Addresses the bucket in the path (<c>{endpoint}/{bucket}/{key}</c>) instead of the host name. MinIO and most
    /// self-hosted stores need it; AWS and R2 do not.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>The access key id. Set with <see cref="SecretAccessKey"/>, or leave both unset to use the SDK's default credential chain.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>The secret access key. Keep it in a secret store or an environment variable, never in <c>appsettings.json</c>.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Credentials set in code, such as an assumed role. Overrides <see cref="AccessKeyId"/> and <see cref="SecretAccessKey"/>.</summary>
    public AWSCredentials? Credentials { get; set; }

    /// <summary><see langword="true"/> when requests go to AWS itself rather than an S3-compatible store.</summary>
    public bool IsAwsEndpoint =>
        ServiceUrl is null
        || ServiceUrl.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase)
        || ServiceUrl.Host.EndsWith(".amazonaws.com.cn", StringComparison.OrdinalIgnoreCase);
}
