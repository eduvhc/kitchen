using ResultingKit;

namespace StoringKit.UnitTests;

/// <summary>An in-memory provider: enough of a store to exercise what <see cref="ObjectStore"/> does around it.</summary>
internal sealed class FakeObjectStore(Uri? publicBaseUrl = null, TimeProvider? timeProvider = null)
    : ObjectStore(publicBaseUrl, timeProvider), IDisposable
{
    private readonly Dictionary<string, (byte[] Bytes, StoredObject Properties)> _objects = new(StringComparer.Ordinal);
    private int _version;

    public List<ObjectWrite> Writes { get; } = [];

    public List<(string Key, DateTimeOffset ExpiresOn)> SignedUrls { get; } = [];

    /// <summary>Opens the stream a get returns over an object's bytes.</summary>
    public Func<byte[], Stream> OpenRead { get; set; } = bytes => new MemoryStream(bytes);

    /// <summary>Every stream a get has returned, to check they were disposed.</summary>
    public List<Stream> Opened { get; } = [];

    /// <summary>Thrown from every core method when set.</summary>
    public Exception? Throw { get; set; }

    /// <summary>What <see cref="TranslateException"/> returns for <see cref="Throw"/>.</summary>
    public Func<Exception, Error?> Translator { get; set; } = _ => null;

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;

    protected override async Task<PutReceipt> PutCoreAsync(ObjectWrite write, CancellationToken ct)
    {
        ThrowIfSet();
        Writes.Add(write);

        if (write.Condition is { } condition
            && (condition.IsCreateOnly
                ? _objects.ContainsKey(write.Key)
                : !_objects.TryGetValue(write.Key, out var current) || current.Properties.ETag != condition.IfMatchETag))
        {
            throw new ConditionNotMetException();
        }

        using var buffer = new MemoryStream();
        await write.Content.CopyToAsync(buffer, ct);

        var etag = $"\"{++_version}\"";
        _objects[write.Key] = (buffer.ToArray(), new StoredObject
        {
            Key = write.Key,
            Size = buffer.Length,
            ContentType = write.ContentType,
            ETag = etag,
            LastModified = TimeProvider.GetUtcNow(),
            Metadata = write.Metadata,
            ContentMd5 = write.ContentMd5,
        });

        return new PutReceipt { Key = write.Key, ETag = etag, Size = buffer.Length, ContentMd5 = write.ContentMd5 };
    }

    protected override Task<ObjectContent> GetCoreAsync(string key, CancellationToken ct)
    {
        ThrowIfSet();
        var (bytes, properties) = _objects[key];
        var stream = OpenRead(bytes);
        Opened.Add(stream);
        return Task.FromResult(new ObjectContent(properties, stream));
    }

    protected override Task<StoredObject> GetPropertiesCoreAsync(string key, CancellationToken ct)
    {
        ThrowIfSet();
        return Task.FromResult(_objects[key].Properties);
    }

    protected override Task<bool> ExistsCoreAsync(string key, CancellationToken ct)
    {
        ThrowIfSet();
        return Task.FromResult(_objects.ContainsKey(key));
    }

    protected override Task DeleteCoreAsync(string key, CancellationToken ct)
    {
        ThrowIfSet();
        _objects.Remove(key);
        return Task.CompletedTask;
    }

    protected override Task<ObjectPage> ListCoreAsync(string? prefix, int pageSize, string? continuationToken, CancellationToken ct)
    {
        ThrowIfSet();
        var page = _objects.Values
            .Select(o => o.Properties)
            .Where(p => prefix is null || p.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new ObjectSummary(p.Key, p.Size, p.ETag, p.LastModified))
            .ToList();

        return Task.FromResult(new ObjectPage(page.Take(pageSize).ToList(), page.Count > pageSize ? "more" : null));
    }

    protected override Task<Uri> GetReadUrlCoreAsync(string key, DateTimeOffset expiresOn, CancellationToken ct)
    {
        ThrowIfSet();
        SignedUrls.Add((key, expiresOn));
        return Task.FromResult(new Uri($"https://signed.example/{Uri.EscapeDataString(key)}"));
    }

    protected override Error? TranslateException(Exception exception, ObjectStoreOperation operation, string key, WriteCondition? condition) =>
        exception switch
        {
            KeyNotFoundException => StoringErrors.MissingObject(key),
            ConditionNotMetException => StoringErrors.ConditionNotMet(key, condition),
            _ => Translator(exception),
        };

    private void ThrowIfSet()
    {
        if (Throw is not null)
        {
            throw Throw;
        }
    }

    private sealed class ConditionNotMetException : Exception;
}
