using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

/// <summary>Describes why an operation failed.</summary>
/// <param name="Code">
/// A stable, machine-readable identifier such as <c>smtp.rejected</c>. Use it in logs, metrics and
/// <c>switch</c> statements.
/// </param>
/// <param name="Message">A human-readable description. Not stable; do not branch on it.</param>
/// <param name="Kind">The category of failure. Decides <see cref="IsTransient"/>.</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "VB-only keyword.")]
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
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

    /// <summary>Returns <c>"{Code}: {Message}"</c>.</summary>
    public override string ToString() => $"{Code}: {Message}";
}
