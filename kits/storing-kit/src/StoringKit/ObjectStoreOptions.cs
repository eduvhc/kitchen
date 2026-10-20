namespace StoringKit;

/// <summary>Settings every provider's options share.</summary>
public abstract class ObjectStoreOptions
{
    /// <summary>
    /// The URL objects in this store are publicly readable under, when the bucket or container has been made public
    /// outside StoringKit: an Azure container with blob-level public access, an R2 bucket on a custom domain or
    /// <c>r2.dev</c>, an S3 bucket behind a public-read bucket policy or CloudFront. <see cref="IObjectStore.GetPublicUrl"/>
    /// appends the escaped key to it. Leave it unset for private stores.
    /// </summary>
    /// <remarks>
    /// Access belongs to the store, not to each object: S3 and R2 have no per-object public flag that works
    /// everywhere, so public and private objects go in separate named stores.
    /// </remarks>
    public Uri? PublicBaseUrl { get; set; }

    /// <summary>Checks <see cref="PublicBaseUrl"/>; providers call it from their own options validation.</summary>
    /// <returns><see langword="true"/> when it is unset or an absolute http(s) URL.</returns>
    public bool HasValidPublicBaseUrl() =>
        PublicBaseUrl is null
        || (PublicBaseUrl.IsAbsoluteUri && (PublicBaseUrl.Scheme == Uri.UriSchemeHttps || PublicBaseUrl.Scheme == Uri.UriSchemeHttp));
}
