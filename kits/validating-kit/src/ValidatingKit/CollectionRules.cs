using System.Collections;

namespace ValidatingKit;

/// <summary>
/// Rules for collection fields: counts, and <c>Each</c> to validate every element under an indexed path such as
/// <c>items[2]</c>.
/// </summary>
public static class CollectionRules
{
    /// <summary>Fails when the collection is <see langword="null"/> or has no elements.</summary>
    /// <typeparam name="TCollection">The collection type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="message">The failure message. Defaults to <c>Must not be empty.</c></param>
    public static Field<TCollection> NotEmpty<TCollection>(this Field<TCollection> field, string? message = null)
        where TCollection : IEnumerable =>
        field.HasFailed || (field.Value is not null && Count(field.Value, 1) > 0) ? field : field.Fail(message ?? Messages.NotEmpty);

    /// <summary>Fails when the collection has fewer than <paramref name="min"/> elements.</summary>
    /// <typeparam name="TCollection">The collection type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="min">The minimum count.</param>
    /// <param name="message">The failure message. Defaults to <c>Must have at least {min} items.</c></param>
    public static Field<TCollection> MinLength<TCollection>(this Field<TCollection> field, int min, string? message = null)
        where TCollection : IEnumerable =>
        field.Skip || Count(field.Value!, min) >= min ? field : field.Fail(message ?? Messages.MinCount(min));

    /// <summary>Fails when the collection has more than <paramref name="max"/> elements.</summary>
    /// <typeparam name="TCollection">The collection type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="max">The maximum count.</param>
    /// <param name="message">The failure message. Defaults to <c>Must have at most {max} items.</c></param>
    public static Field<TCollection> MaxLength<TCollection>(this Field<TCollection> field, int max, string? message = null)
        where TCollection : IEnumerable =>
        field.Skip || Count(field.Value!, max + 1) <= max ? field : field.Fail(message ?? Messages.MaxCount(max));

    /// <summary>Fails unless the collection has exactly <paramref name="length"/> elements.</summary>
    /// <typeparam name="TCollection">The collection type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="length">The required count.</param>
    /// <param name="message">The failure message. Defaults to <c>Must have exactly {length} items.</c></param>
    public static Field<TCollection> Length<TCollection>(this Field<TCollection> field, int length, string? message = null)
        where TCollection : IEnumerable =>
        field.Skip || Count(field.Value!, length + 1) == length ? field : field.Fail(message ?? Messages.ExactCount(length));

    /// <summary>Fails unless the collection has between <paramref name="min"/> and <paramref name="max"/> elements, inclusive.</summary>
    /// <typeparam name="TCollection">The collection type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="min">The minimum count.</param>
    /// <param name="max">The maximum count.</param>
    /// <param name="message">The failure message. Defaults to <c>Must have between {min} and {max} items.</c></param>
    public static Field<TCollection> Length<TCollection>(this Field<TCollection> field, int min, int max, string? message = null)
        where TCollection : IEnumerable
    {
        if (field.Skip)
        {
            return field;
        }

        var count = Count(field.Value!, max + 1);
        return count >= min && count <= max ? field : field.Fail(message ?? Messages.CountBetween(min, max));
    }

    /// <summary>Applies <paramref name="rules"/> to every element, each under the path <c>{field}[{index}]</c>.</summary>
    /// <typeparam name="TItem">The element type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="rules">The element rules, such as <c>i =&gt; i.NotBlank()</c> or <c>i =&gt; i.Valid(lineValidator)</c>.</param>
    public static Field<TItem[]> Each<TItem>(this Field<TItem[]> field, Func<Field<TItem>, Field<TItem>> rules) => field.EachCore<TItem>(rules);

    /// <inheritdoc cref="Each{TItem}(Field{TItem[]}, Func{Field{TItem}, Field{TItem}})"/>
    public static Field<List<TItem>> Each<TItem>(this Field<List<TItem>> field, Func<Field<TItem>, Field<TItem>> rules) => field.EachCore<TItem>(rules);

    /// <inheritdoc cref="Each{TItem}(Field{TItem[]}, Func{Field{TItem}, Field{TItem}})"/>
    public static Field<IList<TItem>> Each<TItem>(this Field<IList<TItem>> field, Func<Field<TItem>, Field<TItem>> rules) => field.EachCore<TItem>(rules);

    /// <inheritdoc cref="Each{TItem}(Field{TItem[]}, Func{Field{TItem}, Field{TItem}})"/>
    public static Field<IReadOnlyList<TItem>> Each<TItem>(this Field<IReadOnlyList<TItem>> field, Func<Field<TItem>, Field<TItem>> rules) => field.EachCore<TItem>(rules);

    /// <inheritdoc cref="Each{TItem}(Field{TItem[]}, Func{Field{TItem}, Field{TItem}})"/>
    public static Field<ICollection<TItem>> Each<TItem>(this Field<ICollection<TItem>> field, Func<Field<TItem>, Field<TItem>> rules) => field.EachCore<TItem>(rules);

    /// <inheritdoc cref="Each{TItem}(Field{TItem[]}, Func{Field{TItem}, Field{TItem}})"/>
    public static Field<IReadOnlyCollection<TItem>> Each<TItem>(this Field<IReadOnlyCollection<TItem>> field, Func<Field<TItem>, Field<TItem>> rules) => field.EachCore<TItem>(rules);

    /// <inheritdoc cref="Each{TItem}(Field{TItem[]}, Func{Field{TItem}, Field{TItem}})"/>
    public static Field<IEnumerable<TItem>> Each<TItem>(this Field<IEnumerable<TItem>> field, Func<Field<TItem>, Field<TItem>> rules) => field.EachCore<TItem>(rules);

    /// <summary>Counts up to <paramref name="limit"/> elements (all of them when it overflowed to negative); a collection that knows its count is not enumerated.</summary>
    private static int Count(IEnumerable items, int limit)
    {
        if (items is ICollection collection)
        {
            return collection.Count;
        }

        var count = 0;
        var enumerator = items.GetEnumerator();
        try
        {
            while ((limit < 0 || count < limit) && enumerator.MoveNext())
            {
                count++;
            }
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }

        return count;
    }
}
