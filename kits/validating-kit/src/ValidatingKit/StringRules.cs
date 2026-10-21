using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ValidatingKit;

/// <summary>Rules for <see cref="string"/> fields. Lengths count UTF-16 characters, as <see cref="string.Length"/> does.</summary>
public static class StringRules
{
    private static readonly TimeSpan s_patternTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Fails when the value is <see langword="null"/>, empty, or only white space.</summary>
    /// <param name="field">The field.</param>
    /// <param name="message">The failure message. Defaults to <c>Must not be blank.</c></param>
    public static Field<string> NotBlank(this Field<string> field, string? message = null) =>
        field.HasFailed || !string.IsNullOrWhiteSpace(field.Value) ? field : field.Fail(message ?? Messages.NotBlank);

    /// <summary>Fails when the value is <see langword="null"/> or empty. White space passes; use <see cref="NotBlank"/> to reject it.</summary>
    /// <param name="field">The field.</param>
    /// <param name="message">The failure message. Defaults to <c>Must not be empty.</c></param>
    public static Field<string> NotEmpty(this Field<string> field, string? message = null) =>
        field.HasFailed || !string.IsNullOrEmpty(field.Value) ? field : field.Fail(message ?? Messages.NotEmpty);

    /// <summary>Fails when the value is shorter than <paramref name="min"/> characters.</summary>
    /// <param name="field">The field.</param>
    /// <param name="min">The minimum length.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be at least {min} characters long.</c></param>
    public static Field<string> MinLength(this Field<string> field, int min, string? message = null) =>
        field.Skip || field.Value!.Length >= min ? field : field.Fail(message ?? Messages.MinLength(min));

    /// <summary>Fails when the value is longer than <paramref name="max"/> characters.</summary>
    /// <param name="field">The field.</param>
    /// <param name="max">The maximum length.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be at most {max} characters long.</c></param>
    public static Field<string> MaxLength(this Field<string> field, int max, string? message = null) =>
        field.Skip || field.Value!.Length <= max ? field : field.Fail(message ?? Messages.MaxLength(max));

    /// <summary>Fails unless the value is exactly <paramref name="length"/> characters long.</summary>
    /// <param name="field">The field.</param>
    /// <param name="length">The required length.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be exactly {length} characters long.</c></param>
    public static Field<string> Length(this Field<string> field, int length, string? message = null) =>
        field.Skip || field.Value!.Length == length ? field : field.Fail(message ?? Messages.ExactLength(length));

    /// <summary>Fails unless the value is between <paramref name="min"/> and <paramref name="max"/> characters long, inclusive.</summary>
    /// <param name="field">The field.</param>
    /// <param name="min">The minimum length.</param>
    /// <param name="max">The maximum length.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be between {min} and {max} characters long.</c></param>
    public static Field<string> Length(this Field<string> field, int min, int max, string? message = null) =>
        field.Skip || (field.Value!.Length >= min && field.Value.Length <= max)
            ? field
            : field.Fail(message ?? Messages.LengthBetween(min, max));

    /// <summary>Fails unless <paramref name="pattern"/> matches the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="pattern">The expression. A <c>[GeneratedRegex]</c> is the cheapest choice.</param>
    /// <param name="message">The failure message. Defaults to <c>Is not in the correct format.</c></param>
    /// <remarks>A match that exceeds the expression's timeout counts as no match.</remarks>
    public static Field<string> Matches(this Field<string> field, Regex pattern, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return field.Skip || IsMatch(pattern, field.Value!) ? field : field.Fail(message ?? Messages.Format);
    }

    /// <summary>Fails unless <paramref name="pattern"/> matches the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="pattern">The expression. Compiled once into .NET's shared regex cache, and matched with a one-second timeout.</param>
    /// <param name="message">The failure message. Defaults to <c>Is not in the correct format.</c></param>
    /// <remarks>A match that exceeds the timeout counts as no match.</remarks>
    public static Field<string> Matches(this Field<string> field, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (field.Skip)
        {
            return field;
        }

        bool matched;
        try
        {
            matched = Regex.IsMatch(field.Value!, pattern, RegexOptions.CultureInvariant, s_patternTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            matched = false;
        }

        return matched ? field : field.Fail(message ?? Messages.Format);
    }

    /// <summary>
    /// Fails unless the value looks like an email address: one <c>@</c> with text on both sides, no white space, and
    /// a domain that does not start or end with a dot.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be a valid email address.</c></param>
    /// <remarks>Deliberately loose, like <c>EmailAddressAttribute</c>. Only sending a message proves an address works.</remarks>
    public static Field<string> Email(this Field<string> field, string? message = null) =>
        field.Skip || IsEmail(field.Value!) ? field : field.Fail(message ?? Messages.Email);

    private static bool IsMatch(Regex pattern, string value)
    {
        try
        {
            return pattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == value.Length - 1 || value.IndexOf('@', at + 1) >= 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return value[at + 1] != '.' && value[^1] != '.';
    }
}
