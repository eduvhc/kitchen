namespace ValidatingKit;

/// <summary>
/// Range and comparison rules for any <see cref="IComparable{T}"/>: numbers, <see cref="DateTime"/>,
/// <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>, <see cref="TimeOnly"/>, <see cref="TimeSpan"/> and more.
/// Each has an overload for the nullable form, which passes <see langword="null"/>.
/// </summary>
public static class ComparisonRules
{
    /// <summary>Fails unless the value is greater than <paramref name="limit"/>.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="limit">The exclusive lower bound.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be greater than {limit}.</c></param>
    public static Field<TValue> GreaterThan<TValue>(this Field<TValue> field, TValue limit, string? message = null)
        where TValue : IComparable<TValue> =>
        field.Skip || field.Value!.CompareTo(limit) > 0 ? field : field.Fail(message ?? Messages.GreaterThan(limit));

    /// <inheritdoc cref="GreaterThan{TValue}(Field{TValue}, TValue, string)"/>
    public static Field<TValue?> GreaterThan<TValue>(this Field<TValue?> field, TValue limit, string? message = null)
        where TValue : struct, IComparable<TValue> =>
        field.Skip || field.Value!.Value.CompareTo(limit) > 0 ? field : field.Fail(message ?? Messages.GreaterThan(limit));

    /// <summary>Fails unless the value is greater than or equal to <paramref name="limit"/>.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="limit">The inclusive lower bound.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be greater than or equal to {limit}.</c></param>
    public static Field<TValue> GreaterThanOrEqualTo<TValue>(this Field<TValue> field, TValue limit, string? message = null)
        where TValue : IComparable<TValue> =>
        field.Skip || field.Value!.CompareTo(limit) >= 0 ? field : field.Fail(message ?? Messages.GreaterThanOrEqualTo(limit));

    /// <inheritdoc cref="GreaterThanOrEqualTo{TValue}(Field{TValue}, TValue, string)"/>
    public static Field<TValue?> GreaterThanOrEqualTo<TValue>(this Field<TValue?> field, TValue limit, string? message = null)
        where TValue : struct, IComparable<TValue> =>
        field.Skip || field.Value!.Value.CompareTo(limit) >= 0 ? field : field.Fail(message ?? Messages.GreaterThanOrEqualTo(limit));

    /// <summary>Fails unless the value is less than <paramref name="limit"/>.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="limit">The exclusive upper bound.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be less than {limit}.</c></param>
    public static Field<TValue> LessThan<TValue>(this Field<TValue> field, TValue limit, string? message = null)
        where TValue : IComparable<TValue> =>
        field.Skip || field.Value!.CompareTo(limit) < 0 ? field : field.Fail(message ?? Messages.LessThan(limit));

    /// <inheritdoc cref="LessThan{TValue}(Field{TValue}, TValue, string)"/>
    public static Field<TValue?> LessThan<TValue>(this Field<TValue?> field, TValue limit, string? message = null)
        where TValue : struct, IComparable<TValue> =>
        field.Skip || field.Value!.Value.CompareTo(limit) < 0 ? field : field.Fail(message ?? Messages.LessThan(limit));

    /// <summary>Fails unless the value is less than or equal to <paramref name="limit"/>.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="limit">The inclusive upper bound.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be less than or equal to {limit}.</c></param>
    public static Field<TValue> LessThanOrEqualTo<TValue>(this Field<TValue> field, TValue limit, string? message = null)
        where TValue : IComparable<TValue> =>
        field.Skip || field.Value!.CompareTo(limit) <= 0 ? field : field.Fail(message ?? Messages.LessThanOrEqualTo(limit));

    /// <inheritdoc cref="LessThanOrEqualTo{TValue}(Field{TValue}, TValue, string)"/>
    public static Field<TValue?> LessThanOrEqualTo<TValue>(this Field<TValue?> field, TValue limit, string? message = null)
        where TValue : struct, IComparable<TValue> =>
        field.Skip || field.Value!.Value.CompareTo(limit) <= 0 ? field : field.Fail(message ?? Messages.LessThanOrEqualTo(limit));

    /// <summary>Fails unless the value is between <paramref name="min"/> and <paramref name="max"/>, inclusive.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be between {min} and {max}.</c></param>
    public static Field<TValue> Between<TValue>(this Field<TValue> field, TValue min, TValue max, string? message = null)
        where TValue : IComparable<TValue> =>
        field.Skip || (field.Value!.CompareTo(min) >= 0 && field.Value.CompareTo(max) <= 0)
            ? field
            : field.Fail(message ?? Messages.Between(min, max));

    /// <inheritdoc cref="Between{TValue}(Field{TValue}, TValue, TValue, string)"/>
    public static Field<TValue?> Between<TValue>(this Field<TValue?> field, TValue min, TValue max, string? message = null)
        where TValue : struct, IComparable<TValue> =>
        field.Skip || (field.Value!.Value.CompareTo(min) >= 0 && field.Value.Value.CompareTo(max) <= 0)
            ? field
            : field.Fail(message ?? Messages.Between(min, max));
}
