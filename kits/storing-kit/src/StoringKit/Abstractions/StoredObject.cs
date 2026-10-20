namespace StoringKit;

/// <summary>The properties of a stored object.</summary>
public sealed record StoredObject
{
    /// <summary>The object key.</summary>
    public required string Key { get; init; }

    /// <summary>The content length in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>The MIME type the object was stored with.</summary>
    public required string ContentType { get; init; }

    /// <summary>The current version's ETag. Opaque; pass it back unchanged to <see cref="WriteCondition.IfMatch"/>.</summary>
    public required string ETag { get; init; }

    /// <summary>When the object was last written.</summary>
    public required DateTimeOffset LastModified { get; init; }

    /// <summary>User metadata, with lower-cased names.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The Base64 MD5 of the content, when the object was written with one. Objects written by other tools, and Azure
    /// uploads from non-seekable streams without <see cref="PutObjectOptions.ContentMd5"/>, may have none.
    /// </summary>
    public string? ContentMd5 { get; init; }
}

/// <summary>What the store reported after a successful <see cref="IObjectStore.PutAsync"/>.</summary>
public sealed record PutReceipt
{
    /// <summary>The key written.</summary>
    public required string Key { get; init; }

    /// <summary>The new version's ETag. Opaque; pass it back unchanged to <see cref="WriteCondition.IfMatch"/>.</summary>
    public required string ETag { get; init; }

    /// <summary>The number of bytes stored.</summary>
    public required long Size { get; init; }

    /// <summary>The Base64 MD5 the content was sent with, or <see langword="null"/> when none was known.</summary>
    public string? ContentMd5 { get; init; }
}

/// <summary>An open object: its properties and a stream over its bytes. Dispose it to release the connection.</summary>
public sealed class ObjectContent : IAsyncDisposable, IDisposable
{
    /// <summary>Wraps an open download.</summary>
    /// <param name="properties">The object's properties as of the read.</param>
    /// <param name="content">The response stream; owned and disposed by this instance.</param>
    public ObjectContent(StoredObject properties, Stream content)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(content);

        Properties = properties;
        Content = content;
    }

    /// <summary>The object's properties, read in the same request as the content.</summary>
    public StoredObject Properties { get; }

    /// <summary>The object's bytes, streamed from the provider. Read it once.</summary>
    public Stream Content { get; }

    /// <inheritdoc />
    public void Dispose() => Content.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
