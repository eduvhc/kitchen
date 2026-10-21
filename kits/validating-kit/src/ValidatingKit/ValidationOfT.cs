using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ResultingKit;

namespace ValidatingKit;

/// <summary>
/// A validation in progress: a value and the failures found so far. Start one with
/// <see cref="Validation.That{T}(T, string, string)"/> and chain rules. Return it where a <see cref="Result"/> or
/// <see cref="Result{TValue}"/> is expected and it converts itself, or call <see cref="ToResult"/> or
/// <see cref="ToValueResult"/> to keep chaining.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
/// <para>
/// Every rule runs, so one result reports every invalid field. Within one field, the first failing rule stops
/// the rest. Rules on a <see langword="null"/> value are skipped; <see cref="Required"/> is the rule that fails it.
/// </para>
/// <para>
/// A struct that shares its failures with every copy, so chaining costs no allocation. Each method returns the
/// validation it was called on, so calls can also be made one statement at a time.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "A builder, never compared.")]
public readonly struct Validation<T>
{
    private readonly T? _value;
    private readonly ValidationFailures _failures;

    internal Validation(T? value, ValidationFailures failures)
    {
        _value = value;
        _failures = failures;
    }

    /// <summary>Fails when the value is <see langword="null"/>.</summary>
    /// <param name="message">The failure message. Defaults to <c>Is required.</c></param>
    /// <remarks>
    /// The value passed to <see cref="Validation.That{T}(T, string, string)"/> fails when it is
    /// <see langword="null"/> even without this rule; call it to choose the message or to make the check explicit.
    /// </remarks>
    public Validation<T> Required(string? message = null)
    {
        if (_value is not null)
        {
            return this;
        }

        _failures.Add("", message ?? Messages.Required);
        return this;
    }

    /// <summary>Validates a member, named after it in camelCase: <c>r =&gt; r.StorageId</c> is <c>storageId</c>.</summary>
    /// <typeparam name="TProp">The member's type, without its nullable annotation.</typeparam>
    /// <param name="selector">Reads the member, such as <c>r =&gt; r.StorageId</c> or <c>r =&gt; r.Address.City</c>.</param>
    /// <param name="rules">Applies the rules, such as <c>f =&gt; f.NotBlank().MaxLength(64)</c>.</param>
    /// <param name="selectorExpression">
    /// Filled in by the compiler with the source of <paramref name="selector"/>, from which the field name comes. Do not pass it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="selector"/> is not a member access on its parameter, so no name can be derived. Use
    /// <see cref="Field{TProp}(string, Func{T, TProp}, Func{Field{TProp}, Field{TProp}})"/>.
    /// </exception>
    public Validation<T> Field<TProp>(
        Func<T, TProp?> selector,
        Func<Field<TProp>, Field<TProp>> rules,
        [CallerArgumentExpression(nameof(selector))] string selectorExpression = "")
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(rules);

        return Field(FieldNames.FromSelector(selectorExpression), selector, rules);
    }

    /// <summary>
    /// Validates a member with rules that can read the whole value, for cross-field checks such as
    /// <c>(f, r) =&gt; f.GreaterThan(r.Start)</c>.
    /// </summary>
    /// <typeparam name="TProp">The member's type, without its nullable annotation.</typeparam>
    /// <param name="selector">Reads the member.</param>
    /// <param name="rules">Applies the rules, given the field and the whole value.</param>
    /// <param name="selectorExpression">Filled in by the compiler. Do not pass it.</param>
    /// <exception cref="ArgumentException"><paramref name="selector"/> is not a member access on its parameter.</exception>
    public Validation<T> Field<TProp>(
        Func<T, TProp?> selector,
        Func<Field<TProp>, T, Field<TProp>> rules,
        [CallerArgumentExpression(nameof(selector))] string selectorExpression = "")
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(rules);

        return Field(FieldNames.FromSelector(selectorExpression), selector, rules);
    }

    /// <summary>Validates a value under an explicit field name, for selectors that are not a plain member access.</summary>
    /// <typeparam name="TProp">The value's type, without its nullable annotation.</typeparam>
    /// <param name="name">The field path, such as <c>total</c> or <c>lines.count</c>. Empty means the value itself.</param>
    /// <param name="selector">Reads the value.</param>
    /// <param name="rules">Applies the rules.</param>
    public Validation<T> Field<TProp>(string name, Func<T, TProp?> selector, Func<Field<TProp>, Field<TProp>> rules)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(rules);

        if (_value is not null)
        {
            rules(new Field<TProp>(selector(_value), name, _failures));
        }

        return this;
    }

    /// <summary>Validates a value under an explicit field name, with rules that can read the whole value.</summary>
    /// <typeparam name="TProp">The value's type, without its nullable annotation.</typeparam>
    /// <param name="name">The field path. Empty means the value itself.</param>
    /// <param name="selector">Reads the value.</param>
    /// <param name="rules">Applies the rules, given the field and the whole value.</param>
    public Validation<T> Field<TProp>(string name, Func<T, TProp?> selector, Func<Field<TProp>, T, Field<TProp>> rules)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(rules);

        if (_value is not null)
        {
            rules(new Field<TProp>(selector(_value), name, _failures), _value);
        }

        return this;
    }

    /// <summary>Fails the value itself, not one field, when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="predicate">Checks the whole value. Not called when the value is <see langword="null"/>.</param>
    /// <param name="message">The failure message.</param>
    /// <remarks>To report a cross-field failure against one field, use the <c>Field</c> overload whose rules receive the value.</remarks>
    public Validation<T> Must(Func<T, bool> predicate, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(message);

        if (_value is not null && !predicate(_value))
        {
            _failures.Add("", message);
        }

        return this;
    }

    /// <summary>Applies <paramref name="rules"/> only when <paramref name="condition"/> returns <see langword="true"/>.</summary>
    /// <param name="condition">Decides whether the rules apply. Not called when the value is <see langword="null"/>.</param>
    /// <param name="rules">The rules to apply, such as <c>v =&gt; v.Field(r =&gt; r.Phone, f =&gt; f.NotBlank())</c>.</param>
    public Validation<T> When(Func<T, bool> condition, Func<Validation<T>, Validation<T>> rules)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(rules);

        if (_value is not null && condition(_value))
        {
            rules(this);
        }

        return this;
    }

    /// <summary>Applies the rules of a reusable <paramref name="validator"/> to this value.</summary>
    /// <param name="validator">The validator whose rules to add.</param>
    public Validation<T> Include(IValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        return validator.Apply(this);
    }

    /// <summary>
    /// Returns success, or a failure with an <see cref="ErrorKind.Validation"/> error whose
    /// <see cref="Error.FieldErrors"/> holds every failure by path and whose <see cref="Error.Message"/> lists them
    /// in the order they were found.
    /// </summary>
    /// <remarks>A success is the shared <see cref="Result.Success()"/>, so a valid value allocates nothing here.</remarks>
    public Result ToResult() => ToError() is { } error ? Result.Failure(error) : Result.Success();

    /// <summary>Like <see cref="ToResult"/>, but a success carries the value, so it chains with <c>Bind</c> and <c>Map</c>.</summary>
    public Result<T> ToValueResult() => ToError() is { } error ? Result<T>.Failure(error) : Result<T>.Success(_value!);

    /// <summary>Finishes the validation as <see cref="ToResult"/> does, so a method returning <see cref="Result"/> can return the validation itself.</summary>
    /// <param name="validation">The validation to finish.</param>
    public static implicit operator Result(Validation<T> validation) => validation.ToResult();

    /// <summary>Finishes the validation as <see cref="ToValueResult"/> does, so a method returning <see cref="Result{TValue}"/> can return the validation itself.</summary>
    /// <param name="validation">The validation to finish.</param>
    public static implicit operator Result<T>(Validation<T> validation) => validation.ToValueResult();

    private Error? ToError()
    {
        if (_value is not null || _failures.Count > 0)
        {
            return _failures.ToError();
        }

        _failures.Add("", Messages.Required);
        return _failures.ToError();
    }
}
