using System.Text;
using ResultingKit;

namespace StoringKit;

/// <summary>
/// The key rules on <see cref="IObjectStore"/>: the intersection of what Azure, S3, R2 and MinIO store without
/// rewriting. Azure turns <c>\</c> into <c>/</c> and HTTP clients collapse <c>.</c> segments, so those are refused
/// rather than silently changed.
/// </summary>
internal static class ObjectKeys
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Error? Validate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
        {
            return StoringErrors.BadKey("the key is empty.");
        }

        if (CommonError(key) is { } error)
        {
            return StoringErrors.BadKey(error);
        }

        if (key.EndsWith('/') || key.EndsWith('.'))
        {
            return StoringErrors.BadKey("a key cannot end with '/' or '.'.");
        }

        return null;
    }

    public static Error? ValidatePrefix(string prefix) =>
        CommonError(prefix) is { } error ? StoringErrors.BadKey($"prefix: {error}") : null;

    private static string? CommonError(string value)
    {
        int bytes;

        try
        {
            bytes = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            return "it is not valid Unicode.";
        }

        if (bytes > ObjectStore.MaxKeyBytes)
        {
            return $"it is longer than {ObjectStore.MaxKeyBytes} UTF-8 bytes.";
        }

        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return "it contains a control character.";
            }

            if (c == '\\')
            {
                return "it contains '\\'; use '/' as the separator.";
            }
        }

        if (value.StartsWith('/'))
        {
            return "it starts with '/'.";
        }

        if (value.Contains("//", StringComparison.Ordinal))
        {
            return "it contains an empty segment ('//').";
        }

        foreach (var segment in value.Split('/'))
        {
            if (segment is "." or "..")
            {
                return "it contains a '.' or '..' segment.";
            }
        }

        return null;
    }
}

/// <summary>The metadata rules on <see cref="PutObjectOptions.Metadata"/>.</summary>
internal static class ObjectMetadata
{
    public static IReadOnlyDictionary<string, string> Normalize(IDictionary<string, string> metadata, out Error? error)
    {
        error = null;
        var normalized = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);
        var bytes = 0;

        foreach (var (name, value) in metadata)
        {
            if (!IsValidName(name))
            {
                error = StoringErrors.BadMetadata(
                    $"'{name}' is not a valid name; use letters, digits and underscores, not starting with a digit.");
                return normalized;
            }

            var lower = name.ToLowerInvariant();

            if (!normalized.TryAdd(lower, value))
            {
                error = StoringErrors.BadMetadata($"'{name}' differs from another name only by case.");
                return normalized;
            }

            if (!IsValidValue(value))
            {
                error = StoringErrors.BadMetadata(
                    $"the value of '{lower}' must be non-empty printable ASCII without leading or trailing spaces.");
                return normalized;
            }

            bytes += lower.Length + value.Length;
        }

        if (bytes > ObjectStore.MaxMetadataBytes)
        {
            error = StoringErrors.BadMetadata($"names and values total {bytes} bytes; the limit is {ObjectStore.MaxMetadataBytes}.");
        }

        return normalized;
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsAsciiDigit(name[0]))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidValue(string? value) =>
        !string.IsNullOrEmpty(value)
        && value[0] != ' '
        && value[^1] != ' '
        && value.All(c => c is >= ' ' and <= '~');
}
