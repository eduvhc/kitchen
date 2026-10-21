using System.Runtime.CompilerServices;

namespace ValidatingKit;

/// <summary>
/// Starts validating a value. Chain rules on the returned <see cref="Validation{T}"/>; it converts to a
/// <see cref="ResultingKit.Result"/> or <see cref="ResultingKit.Result{TValue}"/> wherever one is expected.
/// </summary>
/// <example>
/// <code>
/// public static Result Validate(DeleteFileRequested? request) =>
///     Validation.That(request, "INVALID_REQUEST")
///         .Required()
///         .Field(r => r.StorageId, f => f.NotBlank());
/// </code>
/// </example>
public static class Validation
{
    /// <summary>The <see cref="ResultingKit.Error.Code"/> used when none is given: <c>validation.failed</c>.</summary>
    public const string DefaultCode = "validation.failed";

    /// <summary>Starts validating <paramref name="value"/>.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to validate. A <see langword="null"/> value always fails as required.</param>
    /// <param name="code">The <see cref="ResultingKit.Error.Code"/> of the error a failure produces. Defaults to <see cref="DefaultCode"/>.</param>
    /// <param name="name">
    /// The name used for <paramref name="value"/> itself in <see cref="ResultingKit.Error.Message"/>. Defaults to the
    /// argument expression, such as <c>request</c>, or <c>value</c> when that is not a plain name.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is <see langword="null"/> or empty.</exception>
    public static Validation<T> That<T>(T? value, string code = DefaultCode, [CallerArgumentExpression(nameof(value))] string name = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        return new Validation<T>(value, new ValidationFailures(code, name));
    }
}
