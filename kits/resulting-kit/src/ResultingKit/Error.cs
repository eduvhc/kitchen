using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "VB-only keyword.")]
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
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
