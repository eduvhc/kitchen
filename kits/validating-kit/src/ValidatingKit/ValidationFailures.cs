using System.Text;
using ResultingKit;

namespace ValidatingKit;

/// <summary>The failures one validation collects, shared by every nested scope and field.</summary>
internal sealed class ValidationFailures(string code, string rootExpression)
{
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

        var rootName = FieldNames.ForRoot(rootExpression);
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var message = new StringBuilder();

        foreach (var (path, text) in items)
        {
            if (fields.TryGetValue(path, out var existing))
            {
                Array.Resize(ref existing, existing.Length + 1);
                existing[^1] = text;
                fields[path] = existing;
            }
            else
            {
                fields.Add(path, [text]);
            }

            if (message.Length > 0)
            {
                message.Append(' ');
            }

            var subject = path.Length == 0 ? rootName : path;
            if (subject.Length > 0)
            {
                message.Append(subject).Append(": ");
            }

            message.Append(text);
        }

        return Error.Validation(code, message.ToString(), fields);
    }

    private readonly record struct Failure(string Path, string Message);
}
