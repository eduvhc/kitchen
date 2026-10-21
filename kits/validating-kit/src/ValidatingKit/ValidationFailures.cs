using System.Runtime.InteropServices;
using ResultingKit;

namespace ValidatingKit;

/// <summary>The failures one validation collects, shared by every nested scope and field.</summary>
internal sealed class ValidationFailures(string code, string rootExpression)
{
    private const int LinearLimit = 16;

    private List<Failure>? _items;

    public int Count => _items?.Count ?? 0;

    public void Add(string path, string message) => (_items ??= []).Add(new Failure(path, message));

    /// <summary>Rewrites the paths added since <paramref name="start"/> as relative to <paramref name="prefix"/>.</summary>
    public void Prefix(int start, string prefix)
    {
        if (_items is null || prefix.Length == 0)
        {
            return;
        }

        for (var i = start; i < _items.Count; i++)
        {
            _items[i] = _items[i] with { Path = FieldNames.Combine(prefix, _items[i].Path) };
        }
    }

    public Error? ToError()
    {
        if (_items is not { Count: > 0 } items)
        {
            return null;
        }

        var failures = CollectionsMarshal.AsSpan(items);
        var rootName = FieldNames.ForRoot(rootExpression);

        var fields = new string[failures.Length];
        var messages = new string[failures.Length][];
        var distinct = 0;
        var index = failures.Length > LinearLimit ? new Dictionary<string, int>(failures.Length, StringComparer.Ordinal) : null;
        var length = failures.Length - 1;

        foreach (var (path, text) in failures)
        {
            var at = index is null ? FieldErrorMap.IndexOf(fields, distinct, path) : index.GetValueOrDefault(path, -1);
            if (at < 0)
            {
                index?.Add(path, distinct);
                fields[distinct] = path;
                messages[distinct++] = [text];
            }
            else
            {
                Array.Resize(ref messages[at], messages[at].Length + 1);
                messages[at][^1] = text;
            }

            var subject = path.Length == 0 ? rootName : path;
            length += (subject.Length > 0 ? subject.Length + 2 : 0) + text.Length;
        }

        if (distinct < fields.Length)
        {
            Array.Resize(ref fields, distinct);
            Array.Resize(ref messages, distinct);
        }

        var message = string.Create(length, (items, rootName), static (span, state) =>
        {
            var position = 0;
            foreach (var (path, text) in CollectionsMarshal.AsSpan(state.items))
            {
                if (position > 0)
                {
                    span[position++] = ' ';
                }

                var subject = path.Length == 0 ? state.rootName : path;
                if (subject.Length > 0)
                {
                    subject.CopyTo(span[position..]);
                    position += subject.Length;
                    ": ".CopyTo(span[position..]);
                    position += 2;
                }

                text.CopyTo(span[position..]);
                position += text.Length;
            }
        });

        return Error.Validation(code, message, new FieldErrorMap(fields, messages));
    }

    private readonly record struct Failure(string Path, string Message);
}
