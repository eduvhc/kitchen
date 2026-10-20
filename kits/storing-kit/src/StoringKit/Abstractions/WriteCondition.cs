namespace StoringKit;

/// <summary>
/// A precondition the store checks atomically before it commits a write. The building block for idempotent writes:
/// a create-only put succeeds for exactly one of any number of concurrent writers.
/// </summary>
public sealed class WriteCondition
{
    private WriteCondition(string? ifMatch) => IfMatchETag = ifMatch;

    /// <summary>
    /// Write only when no object exists at the key (<c>If-None-Match: *</c>). Loses with
    /// <see cref="StoringErrors.AlreadyExists"/>.
    /// </summary>
    public static WriteCondition IfNotExists { get; } = new(null);

    /// <summary>The ETag the existing object must still have, or <see langword="null"/> for <see cref="IfNotExists"/>.</summary>
    public string? IfMatchETag { get; }

    /// <summary><see langword="true"/> for <see cref="IfNotExists"/>.</summary>
    public bool IsCreateOnly => IfMatchETag is null;

    /// <summary>
    /// Write only when the existing object still has <paramref name="etag"/> (<c>If-Match</c>). Loses with
    /// <see cref="StoringErrors.PreconditionFailed"/> when the object changed or no longer exists.
    /// </summary>
    /// <param name="etag">An ETag exactly as a <see cref="PutReceipt"/> or <see cref="StoredObject"/> returned it.</param>
    public static WriteCondition IfMatch(string etag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        return new WriteCondition(etag);
    }

    /// <inheritdoc />
    public override string ToString() => IsCreateOnly ? "If-None-Match: *" : $"If-Match: {IfMatchETag}";
}
