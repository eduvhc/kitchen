using System.Globalization;
using System.Runtime.CompilerServices;

namespace ValidatingKit;

/// <summary>
/// The default failure messages. Each rule takes a <c>message</c> argument that replaces its default. Values are
/// formatted invariantly through an interpolation handler, so formatting a number or date does not box it.
/// </summary>
internal static class Messages
{
    public const string Required = "Is required.";
    public const string NotBlank = "Must not be blank.";
    public const string NotEmpty = "Must not be empty.";
    public const string Format = "Is not in the correct format.";
    public const string Email = "Must be a valid email address.";
    public const string InEnum = "Must be one of the defined values.";

    public static string Equal<T>(T expected) => Text("Must be equal to ", expected, ".");

    public static string NotEqual<T>(T unexpected) => Text("Must not be equal to ", unexpected, ".");

    public static string GreaterThan<T>(T limit) => Text("Must be greater than ", limit, ".");

    public static string GreaterThanOrEqualTo<T>(T limit) => Text("Must be greater than or equal to ", limit, ".");

    public static string LessThan<T>(T limit) => Text("Must be less than ", limit, ".");

    public static string LessThanOrEqualTo<T>(T limit) => Text("Must be less than or equal to ", limit, ".");

    public static string Between<T>(T min, T max)
    {
        var text = new DefaultInterpolatedStringHandler(18, 2, CultureInfo.InvariantCulture);
        text.AppendLiteral("Must be between ");
        Append(ref text, min);
        text.AppendLiteral(" and ");
        Append(ref text, max);
        text.AppendLiteral(".");
        return text.ToStringAndClear();
    }

    public static string MinLength(int min) => Count("Must be at least ", min, Characters(min), " long.");

    public static string MaxLength(int max) => Count("Must be at most ", max, Characters(max), " long.");

    public static string ExactLength(int length) => Count("Must be exactly ", length, Characters(length), " long.");

    public static string LengthBetween(int min, int max) =>
        string.Create(CultureInfo.InvariantCulture, $"Must be between {min} and {max} characters long.");

    public static string MinCount(int min) => Count("Must have at least ", min, Items(min), ".");

    public static string MaxCount(int max) => Count("Must have at most ", max, Items(max), ".");

    public static string ExactCount(int count) => Count("Must have exactly ", count, Items(count), ".");

    public static string CountBetween(int min, int max) =>
        string.Create(CultureInfo.InvariantCulture, $"Must have between {min} and {max} items.");

    private static string Characters(int n) => n == 1 ? " character" : " characters";

    private static string Items(int n) => n == 1 ? " item" : " items";

    private static string Count(string before, int n, string noun, string after) =>
        string.Create(CultureInfo.InvariantCulture, $"{before}{n}{noun}{after}");

    private static string Text<T>(string before, T value, string after)
    {
        var text = new DefaultInterpolatedStringHandler(before.Length + after.Length, 1, CultureInfo.InvariantCulture);
        text.AppendLiteral(before);
        Append(ref text, value);
        text.AppendLiteral(after);
        return text.ToStringAndClear();
    }

    private static void Append<T>(ref DefaultInterpolatedStringHandler text, T value)
    {
        if (value is null)
        {
            text.AppendLiteral("null");
        }
        else if (typeof(T) == typeof(string))
        {
            text.AppendLiteral("'");
            text.AppendFormatted(value);
            text.AppendLiteral("'");
        }
        else if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTimeOffset) || typeof(T) == typeof(DateOnly)
            || typeof(T) == typeof(TimeOnly) || typeof(T) == typeof(DateTime?) || typeof(T) == typeof(DateTimeOffset?)
            || typeof(T) == typeof(DateOnly?) || typeof(T) == typeof(TimeOnly?))
        {
            text.AppendFormatted(value, "O");
        }
        else
        {
            text.AppendFormatted(value);
        }
    }
}
