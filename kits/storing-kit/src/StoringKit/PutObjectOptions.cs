namespace StoringKit;

/// <summary>How <see cref="IObjectStore.PutAsync"/> writes an object.</summary>
public sealed class PutObjectOptions
{
    /// <summary>The MIME type stored with the object. Defaults to <c>application/octet-stream</c>.</summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// User metadata stored with the object. Names are letters, digits and underscores, not starting with a digit,
    /// and are stored lower-cased; values are printable ASCII without leading or trailing spaces. Names and values
    /// together are at most <see cref="ObjectStore.MaxMetadataBytes"/> bytes.
    /// </summary>
    /// <remarks>Metadata is readable by anyone who can read the object. It is never recorded in telemetry.</remarks>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The Base64 MD5 of the content, when the caller already knows it. The provider sends it with the upload and the
    /// service rejects content that does not match with <see cref="StoringErrors.ChecksumMismatch"/>. When not set,
    /// StoringKit computes it from seekable streams.
    /// </summary>
    public string? ContentMd5 { get; set; }

    /// <summary>
    /// Makes the write conditional: <see cref="WriteCondition.IfNotExists"/> for create-only,
    /// <see cref="WriteCondition.IfMatch"/> for compare-and-swap. <see langword="null"/> overwrites unconditionally.
    /// </summary>
    public WriteCondition? Condition { get; set; }
}
