using Microsoft.AspNetCore.Http;

namespace ResultingKit.AspNetCore;

/// <summary>
/// Maps an <see cref="Error"/> to an HTTP status code. The <c>ToHttpResult</c> and <c>ToActionResult</c>
/// extensions build on these; the problem body itself comes from ASP.NET Core's own problem details services.
/// </summary>
public static class ErrorHttpExtensions
{
    /// <summary>The <c>ProblemDetails.Extensions</c> key that carries <see cref="Error.Code"/>.</summary>
    public const string CodeExtension = "code";

    /// <summary>
    /// Returns the default status code for <paramref name="kind"/>: <see cref="ErrorKind.Validation"/> 400,
    /// <see cref="ErrorKind.Unauthorized"/> 401, <see cref="ErrorKind.Forbidden"/> 403,
    /// <see cref="ErrorKind.NotFound"/> 404, <see cref="ErrorKind.Conflict"/> 409,
    /// <see cref="ErrorKind.Transient"/> 503, and 500 for <see cref="ErrorKind.Failure"/> or anything else.
    /// </summary>
    /// <param name="kind">The kind of failure.</param>
    public static int ToStatusCode(this ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Transient => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>
    /// Returns the default status code for <paramref name="error"/>'s <see cref="Error.Kind"/>. Pass a
    /// function that falls back to this one when only some errors need a different status.
    /// </summary>
    /// <param name="error">The error to map.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    /// <seealso cref="ToStatusCode(ErrorKind)"/>
    public static int ToStatusCode(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Kind.ToStatusCode();
    }

    internal static int ToStatusCode(this Error error, Func<Error, int>? statusCode) =>
        statusCode is null ? error.ToStatusCode() : statusCode(error);
}
