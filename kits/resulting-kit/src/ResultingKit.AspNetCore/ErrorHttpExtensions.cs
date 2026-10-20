using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ResultingKit.AspNetCore;

/// <summary>
/// Maps an <see cref="Error"/> to an HTTP status code and an RFC 9457 <see cref="ProblemDetails"/>.
/// The <c>ToHttpResult</c> and <c>ToActionResult</c> extensions build on these.
/// </summary>
public static class ErrorHttpExtensions
{
    /// <summary>The <see cref="ProblemDetails.Extensions"/> key that carries <see cref="Error.Code"/>.</summary>
    public const string CodeExtension = "code";

    // The same RFC 9110 links ASP.NET Core uses for its own problem details.
    private static readonly Dictionary<int, string> s_types = new()
    {
        [400] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        [401] = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        [403] = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        [404] = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        [405] = "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        [406] = "https://tools.ietf.org/html/rfc9110#section-15.5.7",
        [408] = "https://tools.ietf.org/html/rfc9110#section-15.5.9",
        [409] = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        [412] = "https://tools.ietf.org/html/rfc9110#section-15.5.13",
        [415] = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        [422] = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        [426] = "https://tools.ietf.org/html/rfc9110#section-15.5.22",
        [500] = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        [502] = "https://tools.ietf.org/html/rfc9110#section-15.6.3",
        [503] = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        [504] = "https://tools.ietf.org/html/rfc9110#section-15.6.5",
    };

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

    /// <summary>
    /// Builds an RFC 9457 problem: <c>status</c> from <paramref name="statusCode"/>, <c>title</c> the
    /// status's reason phrase, <c>type</c> its RFC 9110 section when ASP.NET Core knows one,
    /// <c>detail</c> <see cref="Error.Message"/>, and a <c>code</c> extension holding <see cref="Error.Code"/>.
    /// </summary>
    /// <param name="error">The error to describe.</param>
    /// <param name="statusCode">
    /// Picks the status code. <see langword="null"/> uses <see cref="ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static ProblemDetails ToProblemDetails(this Error error, Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = statusCode is null ? error.ToStatusCode() : statusCode(error);
        var title = ReasonPhrases.GetReasonPhrase(status);

        return new ProblemDetails
        {
            Status = status,
            Title = title.Length == 0 ? null : title,
            Type = s_types.GetValueOrDefault(status),
            Detail = error.Message,
            Extensions = { [CodeExtension] = error.Code },
        };
    }
}
