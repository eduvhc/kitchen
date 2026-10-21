using ResultingKit;

namespace ValidatingKit;

/// <summary>
/// Reusable rules for <typeparamref name="T"/>: validate a value on its own, or apply the rules inside another
/// validation, for a nested object or each element of a collection. Derive from <see cref="Validator{T}"/> rather
/// than implementing this directly.
/// </summary>
/// <typeparam name="T">The type validated.</typeparam>
public interface IValidator<T>
{
    /// <summary>Applies the rules to <paramref name="validation"/> and returns it.</summary>
    /// <param name="validation">The validation in progress, at the root or nested under a field.</param>
    Validation<T> Apply(Validation<T> validation);

    /// <summary>Validates <paramref name="value"/>, returning it on success so the result chains with <c>Bind</c>.</summary>
    /// <param name="value">The value. <see langword="null"/> fails as required.</param>
    Result<T> Validate(T? value);
}
