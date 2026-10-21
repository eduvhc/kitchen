using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

/// <summary>Describes why an operation failed.</summary>
/// <param name="Code">
/// A stable, machine-readable identifier such as <c>smtp.rejected</c>. Use it in logs, metrics and
/// <c>switch</c> statements.
/// </param>
/// <param name="Message">A human-readable description. Not stable; do not branch on it.</param>
/// <param name="Kind">The category of failure. Decides <see cref="IsTransient"/>.</param>
/// <remarks>
/// Two errors are equal when <see cref="Code"/>, <see cref="Message"/> and <see cref="Kind"/> are equal and
/// their <see cref="FieldErrors"/> hold the same fields with the same messages in the same order. The order of
/// the fields themselves does not matter.
/// </remarks>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "VB-only keyword.")]
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
    private readonly IReadOnlyDictionary<string, string[]>? _fieldErrors;

    /// <summary>
    /// The messages for each invalid field, keyed by the field's path (such as <c>name</c> or
    /// <c>items[2].sku</c>), or <see langword="null"/> when the error is not about specific fields.
    /// </summary>
    /// <remarks>
    /// An empty map is stored as <see langword="null"/>. The map is kept as given, not copied; treat it as
    /// immutable. <see cref="Message"/> should still describe the whole error on its own, for consumers that
    /// only carry a code and a message.
    /// </remarks>
    public IReadOnlyDictionary<string, string[]>? FieldErrors
    {
        get => _fieldErrors;
        init => _fieldErrors = value is { Count: > 0 } ? value : null;
    }

    /// <summary>
    /// <see langword="true"/> when <see cref="Kind"/> is <see cref="ErrorKind.Transient"/>, meaning a retry
    /// may succeed. Every other kind is permanent.
    /// </summary>
    public bool IsTransient => Kind == ErrorKind.Transient;

    /// <summary>Creates an error of kind <see cref="ErrorKind.Failure"/>.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Failure(string code, string message) => new(code, message);

    /// <summary>Creates an error of kind <see cref="ErrorKind.Validation"/>.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    /// <summary>Creates an error of kind <see cref="ErrorKind.Validation"/> that names the invalid fields.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description of every failure, for consumers that ignore the fields.</param>
    /// <param name="fieldErrors">The messages for each invalid field, keyed by the field's path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fieldErrors"/> is <see langword="null"/>.</exception>
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]> fieldErrors)
    {
        ArgumentNullException.ThrowIfNull(fieldErrors);
        return new(code, message, ErrorKind.Validation) { FieldErrors = fieldErrors };
    }

    /// <summary>Creates an error of kind <see cref="ErrorKind.NotFound"/>.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    /// <summary>Creates an error of kind <see cref="ErrorKind.Conflict"/>.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    /// <summary>Creates an error of kind <see cref="ErrorKind.Unauthorized"/>.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    /// <summary>Creates an error of kind <see cref="ErrorKind.Forbidden"/>.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    /// <summary>Creates an error of kind <see cref="ErrorKind.Transient"/>, the only kind that is retried.</summary>
    /// <param name="code">A stable, machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Transient(string code, string message) => new(code, message, ErrorKind.Transient);

    /// <summary>
    /// Compares <see cref="Code"/>, <see cref="Message"/>, <see cref="Kind"/> and the contents of
    /// <see cref="FieldErrors"/>.
    /// </summary>
    /// <param name="other">The error to compare with.</param>
    public bool Equals(Error? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && Kind == other.Kind
            && string.Equals(Code, other.Code, StringComparison.Ordinal)
            && string.Equals(Message, other.Message, StringComparison.Ordinal)
            && FieldErrorsEqual(_fieldErrors, other._fieldErrors));

    /// <summary>Hashes <see cref="Code"/>, <see cref="Message"/> and <see cref="Kind"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(Code, Message, Kind);

    /// <summary>Returns <c>"{Code}: {Message}"</c>.</summary>
    public override string ToString() => $"{Code}: {Message}";

    private static bool FieldErrorsEqual(IReadOnlyDictionary<string, string[]>? left, IReadOnlyDictionary<string, string[]>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (field, messages) in left)
        {
            if (!right.TryGetValue(field, out var others) || !messages.AsSpan().SequenceEqual(others))
            {
                return false;
            }
        }

        return true;
    }
}
