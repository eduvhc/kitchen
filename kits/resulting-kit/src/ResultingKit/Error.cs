using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

/// <summary>
/// Why an operation failed. <see cref="Code"/> is stable and machine-readable — <c>smtp.mailbox_unavailable</c>
/// — while <see cref="Message"/> is for people and may change.
/// </summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Error is a VB keyword only. The kits are C#, and Error is the name every result library uses.")]
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
    /// <summary>
    /// True when retrying might succeed. Every other kind is permanent: a retry ladder should
    /// dead-letter it rather than burn attempts.
    /// </summary>
    public bool IsTransient => Kind == ErrorKind.Transient;

    public static Error Failure(string code, string message) => new(code, message);

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error Transient(string code, string message) => new(code, message, ErrorKind.Transient);

    public override string ToString() => $"{Code}: {Message}";
}
