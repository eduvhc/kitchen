using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ValidatingKit;

/// <summary>
/// Turns the source text of a selector, such as <c>r =&gt; r.StorageId</c>, into a camelCase field path. The text
/// comes from <c>CallerArgumentExpression</c>, so naming needs neither reflection nor expression trees.
/// </summary>
internal static class FieldNames
{
    // The compiler passes selector text as a literal, so the same interned instance arrives on every call. A slot
    // table keyed by that reference answers without hashing the text, locking or allocating; a miss, a collision or
    // a non-literal falls back to the ordinal dictionary, which is always correct.
    private const int SlotMask = 2047;
    private static readonly Entry?[] s_slots = new Entry?[SlotMask + 1];
    private static readonly ConcurrentDictionary<string, Entry> s_selectors = new(StringComparer.Ordinal);

    public static string FromSelector(string expression)
    {
        var slot = RuntimeHelpers.GetHashCode(expression) & SlotMask;
        if (s_slots[slot] is { } hit && ReferenceEquals(hit.Expression, expression))
        {
            return hit.Name;
        }

        var neighbour = slot ^ 1;
        if (s_slots[neighbour] is { } near && ReferenceEquals(near.Expression, expression))
        {
            return near.Name;
        }

        var entry = s_selectors.GetOrAdd(expression, static e => new Entry(e, Parse(e)));
        if (ReferenceEquals(entry.Expression, expression))
        {
            s_slots[s_slots[slot] is null || s_slots[neighbour] is not null ? slot : neighbour] = entry;
        }

        return entry.Name;
    }

    /// <summary>Returns the name for a validated value itself: the argument text if it is a plain name, else <c>value</c>.</summary>
    public static string ForRoot(string expression)
    {
        var name = expression.Trim();
        return name.Length > 0 && name.Split('.').All(IsIdentifier) ? name : "value";
    }

    public static string ForType(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return JsonNamingPolicy.CamelCase.ConvertName(tick < 0 ? name : name[..tick]);
    }

    public static string Combine(string prefix, string path) =>
        path.Length == 0 ? prefix
        : prefix.Length == 0 ? path
        : path[0] == '[' ? prefix + path
        : prefix + "." + path;

    private static string Parse(string expression)
    {
        var arrow = expression.IndexOf("=>", StringComparison.Ordinal);
        if (arrow > 0)
        {
            var parameter = expression[..arrow].Trim();
            if (parameter.StartsWith("static ", StringComparison.Ordinal))
            {
                parameter = parameter["static ".Length..].Trim();
            }

            parameter = parameter.Trim('(', ')').Trim();
            parameter = parameter[(parameter.LastIndexOf(' ') + 1)..];

            var segments = expression[(arrow + 2)..].Trim().Split('.');
            if (IsIdentifier(parameter) && segments.Length > 0 && Unwrap(segments[0]) == parameter)
            {
                var names = new string[segments.Length - 1];
                for (var i = 1; i < segments.Length; i++)
                {
                    var segment = Unwrap(segments[i]);
                    if (!IsIdentifier(segment))
                    {
                        names = null;
                        break;
                    }

                    names[i - 1] = JsonNamingPolicy.CamelCase.ConvertName(segment.TrimStart('@'));
                }

                if (names is not null)
                {
                    return string.Join('.', names);
                }
            }
        }

        throw new ArgumentException(
            $"Cannot name a field from '{expression}'. Use a member access such as 'x => x.Name', or pass the name: Field(\"name\", x => ..., f => ...).",
            nameof(expression));
    }

    private static string Unwrap(string segment) => segment.Trim().TrimEnd('!', '?');

    private static bool IsIdentifier(string text)
    {
        var name = text.StartsWith('@') ? text[1..] : text;
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Entry(string expression, string name)
    {
        public string Expression { get; } = expression;

        public string Name { get; } = name;
    }
}
