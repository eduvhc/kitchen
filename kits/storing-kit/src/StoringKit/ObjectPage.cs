namespace StoringKit;

/// <summary>One page of <see cref="IObjectStore.ListAsync"/>.</summary>
/// <param name="Objects">The objects on this page, in ascending key order.</param>
/// <param name="ContinuationToken">
/// Pass to <see cref="ListObjectsOptions.ContinuationToken"/> for the next page; <see langword="null"/> on the last.
/// </param>
public sealed record ObjectPage(IReadOnlyList<ObjectSummary> Objects, string? ContinuationToken)
{
    /// <summary><see langword="true"/> when another page follows.</summary>
    public bool HasMore => ContinuationToken is not null;
}

/// <summary>
/// An object as a listing describes it. Content type and metadata need <see cref="IObjectStore.GetPropertiesAsync"/>.
/// </summary>
/// <param name="Key">The object key.</param>
/// <param name="Size">The content length in bytes.</param>
/// <param name="ETag">The current version's ETag.</param>
/// <param name="LastModified">When the object was last written.</param>
public sealed record ObjectSummary(string Key, long Size, string ETag, DateTimeOffset LastModified);

/// <summary>Paging for <see cref="IObjectStore.ListAsync"/>.</summary>
public sealed class ListObjectsOptions
{
    /// <summary>The largest page every provider serves.</summary>
    public const int MaxPageSize = 1000;

    /// <summary>The most objects to return, 1 to <see cref="MaxPageSize"/>. Defaults to <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; set; } = MaxPageSize;

    /// <summary>The <see cref="ObjectPage.ContinuationToken"/> of the previous page, or <see langword="null"/> for the first.</summary>
    public string? ContinuationToken { get; set; }
}
