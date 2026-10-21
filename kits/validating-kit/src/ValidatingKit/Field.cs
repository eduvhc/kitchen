using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ValidatingKit;

/// <summary>
/// One field under validation: its value and whether a rule has already failed it. Rules are methods on this type
/// and extension methods for specific value types; each returns the field, so they chain.
/// </summary>
/// <typeparam name="TProp">The field's type, without its nullable annotation.</typeparam>
/// <remarks>
/// <para>
/// Once a rule fails, the remaining rules on the field are skipped, so each field reports its first failure.
/// </para>
/// <para>
/// Rules other than <see cref="Required"/>, <c>NotBlank</c> and <c>NotEmpty</c> pass a <see langword="null"/> value,
/// so an optional field is validated only when present. Put <see cref="Required"/> first to make it mandatory.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "A builder, never compared.")]
public readonly struct Field<TProp>
{
    private readonly ValidationFailures _failures;
    private readonly string _path;
    private readonly int _index;

    internal Field(TProp? value, string path, ValidationFailures failures, int index = -1, bool failed = false)
    {
        Value = value;
        _path = path;
        _failures = failures;
        _index = index;
        HasFailed = failed;
    }

    /// <summary>The field's value.</summary>
    public TProp? Value { get; }

    /// <summary><see langword="true"/> once a rule has failed this field; later rules are skipped.</summary>
    public bool HasFailed { get; }

    /// <summary><see langword="true"/> when a rule should not run: the field has failed or its value is <see langword="null"/>.</summary>
    internal bool Skip
    {
        get
        {
            // Branching on "is not null" lets the JIT drop the box of a value-type TProp; "is null" as a value does not.
            if (!HasFailed && Value is not null)
            {
                return false;
            }

            return true;
        }
    }

    internal string Path => _index < 0 ? _path : FieldNames.Combine(_path, string.Create(CultureInfo.InvariantCulture, $"[{_index}]"));

    /// <summary>Fails when the value is <see langword="null"/>.</summary>
    /// <param name="message">The failure message. Defaults to <c>Is required.</c></param>
    public Field<TProp> Required(string? message = null) =>
        HasFailed || Value is not null ? this : Fail(message ?? Messages.Required);

    /// <summary>Fails when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="predicate">Checks the value. Not called when the value is <see langword="null"/>.</param>
    /// <param name="message">The failure message.</param>
    public Field<TProp> Must(Func<TProp, bool> predicate, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(message);

        return Skip || predicate(Value!) ? this : Fail(message);
    }

    /// <summary>Fails unless the value equals <paramref name="expected"/>, by <see cref="EqualityComparer{T}.Default"/>.</summary>
    /// <param name="expected">The only valid value.</param>
    /// <param name="message">The failure message. Defaults to <c>Must be equal to {expected}.</c></param>
    public Field<TProp> Equal(TProp expected, string? message = null) =>
        Skip || EqualityComparer<TProp>.Default.Equals(Value!, expected) ? this : Fail(message ?? Messages.Equal(expected));

    /// <summary>Fails when the value equals <paramref name="unexpected"/>, by <see cref="EqualityComparer{T}.Default"/>.</summary>
    /// <param name="unexpected">The value that is not allowed.</param>
    /// <param name="message">The failure message. Defaults to <c>Must not be equal to {unexpected}.</c></param>
    public Field<TProp> NotEqual(TProp unexpected, string? message = null) =>
        Skip || !EqualityComparer<TProp>.Default.Equals(Value!, unexpected) ? this : Fail(message ?? Messages.NotEqual(unexpected));

    /// <summary>
    /// Validates the value's own members, with paths prefixed by this field's: a <c>city</c> failure inside
    /// <c>address</c> is reported as <c>address.city</c>.
    /// </summary>
    /// <param name="rules">The nested rules, such as <c>a =&gt; a.Field(x =&gt; x.City, f =&gt; f.NotBlank())</c>.</param>
    public Field<TProp> Valid(Func<Validation<TProp>, Validation<TProp>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (Skip)
        {
            return this;
        }

        var start = _failures.Count;
        rules(new Validation<TProp>(Value, _failures));
        return Nested(start);
    }

    /// <summary>Validates the value with a reusable <paramref name="validator"/>, with paths prefixed by this field's.</summary>
    /// <param name="validator">The validator for the value's type.</param>
    public Field<TProp> Valid(IValidator<TProp> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        if (Skip)
        {
            return this;
        }

        var start = _failures.Count;
        validator.Apply(new Validation<TProp>(Value, _failures));
        return Nested(start);
    }

    internal Field<TProp> Fail(string message)
    {
        _failures.Add(Path, message);
        return new Field<TProp>(Value, _path, _failures, _index, failed: true);
    }

    internal Field<TProp> EachCore<TItem>(Func<Field<TItem>, Field<TItem>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (Skip)
        {
            return this;
        }

        var start = _failures.Count;
        var parent = Path;

        switch (Value)
        {
            case IList<TItem> list:
                for (var i = 0; i < list.Count; i++)
                {
                    rules(new Field<TItem>(list[i], parent, _failures, i));
                }

                break;

            case IReadOnlyList<TItem> list:
                for (var i = 0; i < list.Count; i++)
                {
                    rules(new Field<TItem>(list[i], parent, _failures, i));
                }

                break;

            case IEnumerable<TItem> items:
                var index = 0;
                foreach (var item in items)
                {
                    rules(new Field<TItem>(item, parent, _failures, index++));
                }

                break;
        }

        return _failures.Count == start ? this : new Field<TProp>(Value, _path, _failures, _index, failed: true);
    }

    private Field<TProp> Nested(int start)
    {
        if (_failures.Count == start)
        {
            return this;
        }

        _failures.Prefix(start, Path);
        return new Field<TProp>(Value, _path, _failures, _index, failed: true);
    }
}
