using System.Reflection;
using System.Text;

namespace MessagingKit;

/// <summary>
/// Resolves the wire name of a message type: <see cref="MessageAttribute"/> when present, otherwise
/// the kebab-cased type name.
/// </summary>
public static class MessageName
{
    public static string For<TMessage>() => For(typeof(TMessage));

    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.GetCustomAttribute<MessageAttribute>()?.Name ?? ToKebabCase(type.Name);
    }

    /// <summary>SendEmail → send-email, HTTPRequest → http-request.</summary>
    private static string ToKebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (!char.IsUpper(c))
            {
                builder.Append(c);
                continue;
            }

            // Break before an uppercase run that starts a word, and before the last capital of a run
            // that is followed by lowercase (so HTTPRequest splits as http|request).
            var startsWord = i > 0 && !char.IsUpper(name[i - 1]);
            var endsAcronym = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);

            if (startsWord || endsAcronym)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
