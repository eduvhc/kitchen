using System.Globalization;

namespace ValidatingKit;

/// <summary>The default failure messages. Each rule takes a <c>message</c> argument that replaces its default.</summary>
internal static class Messages
{
    public const string Required = "Is required.";
    public const string NotBlank = "Must not be blank.";
    public const string NotEmpty = "Must not be empty.";
    public const string Format = "Is not in the correct format.";
    public const string Email = "Must be a valid email address.";
    public const string InEnum = "Must be one of the defined values.";

    public static string Equal<T>(T expected) => Text($"Must be equal to {Display(expected)}.");

    public static string NotEqual<T>(T unexpected) => Text($"Must not be equal to {Display(unexpected)}.");

    public static string GreaterThan<T>(T limit) => Text($"Must be greater than {Display(limit)}.");

    public static string GreaterThanOrEqualTo<T>(T limit) => Text($"Must be greater than or equal to {Display(limit)}.");

    public static string LessThan<T>(T limit) => Text($"Must be less than {Display(limit)}.");

    public static string LessThanOrEqualTo<T>(T limit) => Text($"Must be less than or equal to {Display(limit)}.");

    public static string Between<T>(T min, T max) => Text($"Must be between {Display(min)} and {Display(max)}.");

    public static string MinLength(int min) => Text($"Must be at least {min} {Characters(min)} long.");

    public static string MaxLength(int max) => Text($"Must be at most {max} {Characters(max)} long.");

    public static string ExactLength(int length) => Text($"Must be exactly {length} {Characters(length)} long.");

    public static string LengthBetween(int min, int max) => Text($"Must be between {min} and {max} characters long.");

    public static string MinCount(int min) => Text($"Must have at least {min} {Items(min)}.");

    public static string MaxCount(int max) => Text($"Must have at most {max} {Items(max)}.");

    public static string ExactCount(int count) => Text($"Must have exactly {count} {Items(count)}.");

    public static string CountBetween(int min, int max) => Text($"Must have between {min} and {max} items.");

    private static string Characters(int n) => n == 1 ? "character" : "characters";

    private static string Items(int n) => n == 1 ? "item" : "items";

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Display<T>(T value) => value switch
    {
        null => "null",
        string text => $"'{text}'",
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
