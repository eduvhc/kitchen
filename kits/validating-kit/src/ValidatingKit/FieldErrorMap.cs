using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace ValidatingKit;

/// <summary>
/// A read-only map of field paths to messages, kept as two arrays in the order the fields first failed. Validation
/// errors hold a handful of fields, so a linear scan beats hashing and the map costs two arrays instead of a
/// dictionary's buckets and entries.
/// </summary>
internal sealed class FieldErrorMap(string[] fields, string[][] messages) : IReadOnlyDictionary<string, string[]>
{
    public int Count => fields.Length;

    public IEnumerable<string> Keys => fields;

    public IEnumerable<string[]> Values => messages;

    public string[] this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"No field errors for '{key}'.");

    public bool ContainsKey(string key) => IndexOf(fields, fields.Length, key) >= 0;

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string[] value)
    {
        var index = IndexOf(fields, fields.Length, key);
        value = index >= 0 ? messages[index] : null;
        return index >= 0;
    }

    public IEnumerator<KeyValuePair<string, string[]>> GetEnumerator()
    {
        for (var i = 0; i < fields.Length; i++)
        {
            yield return new KeyValuePair<string, string[]>(fields[i], messages[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static int IndexOf(string[] fields, int count, string key)
    {
        for (var i = 0; i < count; i++)
        {
            if (string.Equals(fields[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
