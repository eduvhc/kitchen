using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ResultingKit;

namespace StoringKit;

/// <summary>
/// Reads and writes objects as JSON, the way <c>System.Net.Http.Json</c> does for <c>HttpClient</c>. They compose
/// <see cref="IObjectStore.PutAsync"/> and <see cref="IObjectStore.GetAsync"/>, so they behave the same on every
/// provider, return the same errors and are traced like any other put or get.
/// </summary>
/// <remarks>
/// <para>
/// Serializer options default to <see cref="JsonSerializerDefaults.Web"/>: camelCase names, case-insensitive reads,
/// numbers allowed in strings. The <see cref="JsonTypeInfo{T}"/> overloads take source-generated metadata and are
/// safe to trim and compile ahead of time.
/// </para>
/// <para>
/// Stored content that is not valid JSON for the type throws <see cref="JsonException"/>, as
/// <c>HttpClient.GetFromJsonAsync</c> does: retrying cannot fix it, so it is not an expected outcome.
/// </para>
/// </remarks>
public static class ObjectStoreJsonExtensions
{
    private const string ReflectionUnreferencedCode =
        "JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo<T>, or make sure all of the required types are preserved.";

    private const string ReflectionDynamicCode =
        "JSON serialization and deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use System.Text.Json source generation for native AOT applications.";

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads <paramref name="key"/> and deserializes it from JSON.</summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="store">The store.</param>
    /// <param name="key">The object key.</param>
    /// <param name="jsonOptions">Serializer options; <see langword="null"/> for <see cref="JsonSerializerDefaults.Web"/>.</param>
    /// <param name="ct">Cancels the request and the read.</param>
    /// <returns>
    /// The value with the properties read alongside it, including the <see cref="StoredDocument{T}.ETag"/> to write
    /// back with <see cref="WriteCondition.IfMatch"/>; or <see cref="StoringErrors.NotFound"/>, or a
    /// <see cref="ErrorKind.Transient"/> error when the request or the read fails on the way.
    /// </returns>
    /// <exception cref="JsonException">The stored content is not valid JSON, or does not match <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(ReflectionUnreferencedCode)]
    [RequiresDynamicCode(ReflectionDynamicCode)]
    public static Task<Result<StoredDocument<T>>> GetFromJsonAsync<T>(
        this IObjectStore store,
        string key,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken ct = default) =>
        store.GetFromJsonAsync(key, TypeInfo<T>(jsonOptions), ct);

    /// <summary>Reads <paramref name="key"/> and deserializes it from JSON with source-generated metadata.</summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="store">The store.</param>
    /// <param name="key">The object key.</param>
    /// <param name="jsonTypeInfo">The metadata for <typeparamref name="T"/>, such as <c>AppJsonContext.Default.Order</c>.</param>
    /// <param name="ct">Cancels the request and the read.</param>
    /// <returns>
    /// The value with the properties read alongside it, including the <see cref="StoredDocument{T}.ETag"/> to write
    /// back with <see cref="WriteCondition.IfMatch"/>; or <see cref="StoringErrors.NotFound"/>, or a
    /// <see cref="ErrorKind.Transient"/> error when the request or the read fails on the way.
    /// </returns>
    /// <exception cref="JsonException">The stored content is not valid JSON, or does not match <typeparamref name="T"/>.</exception>
    public static async Task<Result<StoredDocument<T>>> GetFromJsonAsync<T>(
        this IObjectStore store,
        string key,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var get = await store.GetAsync(key, ct).ConfigureAwait(false);

        if (get.IsFailure)
        {
            return get.Error;
        }

        await using var content = get.Value;

        try
        {
            var value = await JsonSerializer.DeserializeAsync(content.Content, jsonTypeInfo, ct).ConfigureAwait(false);
            return new StoredDocument<T>(value, content.Properties);
        }
        catch (Exception ex) when (ObjectStore.TranslateReadException(ex, ct) is { } error)
        {
            return error;
        }
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON and writes it to <paramref name="key"/>, replacing any existing
    /// object unless <see cref="PutObjectOptions.Condition"/> says otherwise.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="store">The store.</param>
    /// <param name="key">The object key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="options">
    /// Write condition, metadata and checksum, as for <see cref="IObjectStore.PutAsync"/>. The content type defaults to
    /// <c>application/json</c>. Metadata is written exactly as given: copy <see cref="StoredObject.Metadata"/> in to keep it.
    /// </param>
    /// <param name="jsonOptions">Serializer options; <see langword="null"/> for <see cref="JsonSerializerDefaults.Web"/>.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>
    /// A receipt with the new ETag; or <see cref="StoringErrors.AlreadyExists"/> /
    /// <see cref="StoringErrors.PreconditionFailed"/> when the condition did not hold.
    /// </returns>
    [RequiresUnreferencedCode(ReflectionUnreferencedCode)]
    [RequiresDynamicCode(ReflectionDynamicCode)]
    public static Task<Result<PutReceipt>> PutAsJsonAsync<T>(
        this IObjectStore store,
        string key,
        T value,
        PutObjectOptions? options = null,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken ct = default) =>
        store.PutAsJsonAsync(key, value, TypeInfo<T>(jsonOptions), options, ct);

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON with source-generated metadata and writes it to
    /// <paramref name="key"/>, replacing any existing object unless <see cref="PutObjectOptions.Condition"/> says otherwise.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="store">The store.</param>
    /// <param name="key">The object key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="jsonTypeInfo">The metadata for <typeparamref name="T"/>, such as <c>AppJsonContext.Default.Order</c>.</param>
    /// <param name="options">
    /// Write condition, metadata and checksum, as for <see cref="IObjectStore.PutAsync"/>. The content type defaults to
    /// <c>application/json</c>. Metadata is written exactly as given: copy <see cref="StoredObject.Metadata"/> in to keep it.
    /// </param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>
    /// A receipt with the new ETag; or <see cref="StoringErrors.AlreadyExists"/> /
    /// <see cref="StoringErrors.PreconditionFailed"/> when the condition did not hold.
    /// </returns>
    public static async Task<Result<PutReceipt>> PutAsJsonAsync<T>(
        this IObjectStore store,
        string key,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        PutObjectOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        // A seekable buffer keeps the end-to-end MD5 and spares the S3 provider its temporary file.
        using var content = new MemoryStream();
        JsonSerializer.Serialize(content, value, jsonTypeInfo);
        content.Position = 0;

        return await store.PutAsync(key, content, WithJsonContentType(options), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserializes the content of an open object from JSON. The stream is read to the end but not disposed; the
    /// caller still disposes <paramref name="content"/>. The content type is not checked.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="content">An object opened with <see cref="IObjectStore.GetAsync"/>.</param>
    /// <param name="jsonOptions">Serializer options; <see langword="null"/> for <see cref="JsonSerializerDefaults.Web"/>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The value; <see langword="default"/> when the content is the JSON literal <c>null</c>.</returns>
    /// <exception cref="JsonException">The content is not valid JSON, or does not match <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(ReflectionUnreferencedCode)]
    [RequiresDynamicCode(ReflectionDynamicCode)]
    public static Task<T?> ReadFromJsonAsync<T>(
        this ObjectContent content,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken ct = default) =>
        content.ReadFromJsonAsync(TypeInfo<T>(jsonOptions), ct);

    /// <summary>
    /// Deserializes the content of an open object from JSON with source-generated metadata. The stream is read to the
    /// end but not disposed; the caller still disposes <paramref name="content"/>. The content type is not checked.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="content">An object opened with <see cref="IObjectStore.GetAsync"/>.</param>
    /// <param name="jsonTypeInfo">The metadata for <typeparamref name="T"/>, such as <c>AppJsonContext.Default.Order</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The value; <see langword="default"/> when the content is the JSON literal <c>null</c>.</returns>
    /// <exception cref="JsonException">The content is not valid JSON, or does not match <typeparamref name="T"/>.</exception>
    public static async Task<T?> ReadFromJsonAsync<T>(
        this ObjectContent content,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        return await JsonSerializer.DeserializeAsync(content.Content, jsonTypeInfo, ct).ConfigureAwait(false);
    }

    [RequiresUnreferencedCode(ReflectionUnreferencedCode)]
    [RequiresDynamicCode(ReflectionDynamicCode)]
    private static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions? jsonOptions)
    {
        jsonOptions ??= WebOptions;
        jsonOptions.MakeReadOnly(populateMissingResolver: true);
        return (JsonTypeInfo<T>)jsonOptions.GetTypeInfo(typeof(T));
    }

    private static PutObjectOptions WithJsonContentType(PutObjectOptions? options)
    {
        if (options?.ContentType is not null)
        {
            return options;
        }

        var json = new PutObjectOptions
        {
            ContentType = MediaTypeNames.Application.Json,
            ContentMd5 = options?.ContentMd5,
            Condition = options?.Condition,
        };

        foreach (var (name, value) in options?.Metadata ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            json.Metadata[name] = value;
        }

        return json;
    }
}
