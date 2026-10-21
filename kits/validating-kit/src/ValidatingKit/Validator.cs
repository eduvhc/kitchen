using ResultingKit;

namespace ValidatingKit;

/// <summary>
/// A base for reusable validators. Override <see cref="Apply"/> with the rules; <see cref="Validate"/> runs them
/// with <see cref="Code"/> as the error code.
/// </summary>
/// <typeparam name="T">The type validated.</typeparam>
/// <example>
/// <code>
/// public sealed class AddressValidator() : Validator&lt;Address&gt;("address.invalid")
/// {
///     public override Validation&lt;Address&gt; Apply(Validation&lt;Address&gt; address) => address
///         .Field(a => a.Street, f => f.NotBlank())
///         .Field(a => a.PostalCode, f => f.NotBlank().MaxLength(10));
/// }
/// </code>
/// </example>
/// <remarks>
/// Validators hold no state between calls, so one instance can be shared; register it as a singleton, for example
/// <c>services.AddSingleton&lt;IValidator&lt;Address&gt;, AddressValidator&gt;()</c>.
/// </remarks>
public abstract class Validator<T> : IValidator<T>
{
    private static readonly string s_name = FieldNames.ForType(typeof(T));

    /// <summary>Creates a validator whose failures carry <paramref name="code"/>.</summary>
    /// <param name="code">The <see cref="Error.Code"/> of the error a failure produces.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is <see langword="null"/> or empty.</exception>
    protected Validator(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        Code = code;
    }

    /// <summary>The <see cref="Error.Code"/> of the error a failure produces.</summary>
    public string Code { get; }

    /// <inheritdoc />
    public abstract Validation<T> Apply(Validation<T> validation);

    /// <inheritdoc />
    /// <remarks>A <see langword="null"/> value is named after <typeparamref name="T"/> in the message, such as <c>address: Is required.</c></remarks>
    public Result<T> Validate(T? value) => Apply(Validation.That(value, Code, s_name)).ToValueResult();
}
