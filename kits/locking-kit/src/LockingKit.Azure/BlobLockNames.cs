using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LockingKit.Azure;

/// <summary>
/// Maps any lock key to a guard blob name, injectively: two different keys never share a guard.
/// </summary>
/// <remarks>
/// <para>
/// Letters, digits, <c>-</c>, <c>_</c>, <c>.</c> and <c>/</c> pass through, so <c>orders/42</c> is the blob
/// <c>locks/orders/42</c> and guards stay readable in Storage Explorer. Every other character, and <c>~</c> itself,
/// becomes <c>~XX</c> per UTF-8 byte. A <c>/</c> that would make an empty segment (leading, trailing, doubled) and a
/// <c>.</c> that starts a segment or ends the key are escaped too, because Azure and HTTP clients rewrite those.
/// </para>
/// <para>
/// A name that would exceed Azure's 1024 characters or 254 segments becomes <c>~sha256~{hex}</c> instead. No escaped
/// name contains <c>~s</c>, so the two forms cannot collide.
/// </para>
/// </remarks>
internal static class BlobLockNames
{
    public const int MaxNameLength = 1024;
    public const int MaxSegments = 254;
    public const int MaxPrefixLength = 256;

    private const string HashedMarker = "~sha256~";

    public static string ToBlobName(string prefix, string key)
    {
        var escaped = Escape(key);
        var name = prefix + escaped;

        if (name.Length <= MaxNameLength && name.Count(c => c == '/') < MaxSegments)
        {
            return name;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return prefix + HashedMarker + Convert.ToHexStringLower(hash);
    }

    public static string Escape(string key)
    {
        var builder = new StringBuilder(key.Length + 8);
        Span<byte> utf8 = stackalloc byte[4];

        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            var atSegmentStart = builder.Length == 0 || builder[^1] == '/';
            var isLast = i == key.Length - 1;

            if (c == '/' && !atSegmentStart && !isLast)
            {
                builder.Append(c);
            }
            else if (c == '.' && !atSegmentStart && !isLast)
            {
                builder.Append(c);
            }
            else if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
            else
            {
                // Keys are validated as well-formed UTF-16, so a high surrogate is always followed by its pair.
                var length = char.IsHighSurrogate(c) ? 2 : 1;
                var written = Encoding.UTF8.GetBytes(key.AsSpan(i, length), utf8);

                foreach (var b in utf8[..written])
                {
                    builder.Append('~').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }

                i += length - 1;
            }
        }

        return builder.ToString();
    }

    /// <summary>The inverse of <see cref="Escape"/>, for diagnostics and tests.</summary>
    public static string Unescape(string escaped)
    {
        var bytes = new List<byte>(escaped.Length);

        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] == '~')
            {
                bytes.Add(byte.Parse(escaped.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                i += 2;
            }
            else
            {
                bytes.Add((byte)escaped[i]);
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public static bool IsValidPrefix(string? prefix)
    {
        if (prefix is null)
        {
            return false;
        }

        if (prefix.Length == 0)
        {
            return true;
        }

        return prefix.Length <= MaxPrefixLength
            && prefix.EndsWith('/')
            && !prefix.StartsWith('/')
            && !prefix.Contains("//", StringComparison.Ordinal)
            && prefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/')
            && prefix.Split('/').All(segment => segment is not ("." or ".."));
    }
}
