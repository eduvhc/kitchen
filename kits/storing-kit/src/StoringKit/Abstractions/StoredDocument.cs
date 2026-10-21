namespace StoringKit;

/// <summary>
/// A JSON document read by <see cref="ObjectStoreJsonExtensions.GetFromJsonAsync{T}(IObjectStore, string, System.Text.Json.JsonSerializerOptions?, CancellationToken)"/>:
/// the deserialized value and the properties returned with it.
/// </summary>
/// <typeparam name="T">The document type.</typeparam>
/// <param name="Value">The deserialized value; <see langword="default"/> when the stored JSON is the literal <c>null</c>.</param>
/// <param name="Properties">The object's properties, read in the same request as the content.</param>
public sealed record StoredDocument<T>(T? Value, StoredObject Properties)
{
    /// <summary>
    /// The ETag of the version <see cref="Value"/> was read from. Pass it to <see cref="WriteCondition.IfMatch"/> to
    /// write back only if nobody else has since.
    /// </summary>
    public string ETag => Properties.ETag;
}
