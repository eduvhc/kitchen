using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ResultingKit.AspNetCore;

/// <summary>
/// Turns a <see cref="Result"/> into an <see cref="IResult"/> for minimal APIs. A failure becomes the
/// <see cref="ProblemHttpResult"/> that <see cref="TypedResults.Problem(string, string, int?, string, string, IDictionary{string, object})"/>
/// builds: <c>detail</c> is <see cref="Error.Message"/>, a <c>code</c> extension carries <see cref="Error.Code"/>,
/// and ASP.NET Core fills <c>title</c> and <c>type</c> from the status. When the host registers
/// <c>AddProblemDetails()</c>, it is written through <c>IProblemDetailsService</c>, which adds a <c>traceId</c>
/// and runs <c>ProblemDetailsOptions.CustomizeProblemDetails</c>.
/// </summary>
public static class ResultHttpResultExtensions
{
    /// <summary>Returns 204 No Content on success, or a problem response on failure.</summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="statusCode">
    /// Picks the status code for a failure. <see langword="null"/> uses
    /// <see cref="ErrorHttpExtensions.ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static IResult ToHttpResult(this Result result, Func<Error, int>? statusCode = null) =>
        result.ToHttpResult(static () => TypedResults.NoContent(), statusCode);

    /// <summary>Returns what <paramref name="onSuccess"/> builds on success, or a problem response on failure.</summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="onSuccess">Builds the success response, such as <c>() =&gt; TypedResults.Accepted(...)</c>.</param>
    /// <param name="statusCode">
    /// Picks the status code for a failure. <see langword="null"/> uses
    /// <see cref="ErrorHttpExtensions.ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess, Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess() : Problem(result.Error, statusCode);
    }

    /// <summary>Returns 200 OK with the value on success, or a problem response on failure.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="statusCode">
    /// Picks the status code for a failure. <see langword="null"/> uses
    /// <see cref="ErrorHttpExtensions.ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result, Func<Error, int>? statusCode = null) =>
        result.ToHttpResult(static value => TypedResults.Ok(value), statusCode);

    /// <summary>
    /// Returns what <paramref name="onSuccess"/> builds from the value on success, or a problem response
    /// on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="onSuccess">
    /// Builds the success response from the value, such as
    /// <c>order =&gt; TypedResults.Created($"/orders/{order.Id}", order)</c>.
    /// </param>
    /// <param name="statusCode">
    /// Picks the status code for a failure. <see langword="null"/> uses
    /// <see cref="ErrorHttpExtensions.ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static IResult ToHttpResult<TValue>(
        this Result<TValue> result,
        Func<TValue, IResult> onSuccess,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error, statusCode);
    }

    /// <summary>Awaits <paramref name="result"/>, then converts it as <see cref="ToHttpResult(Result, Func{Error, int})"/> does.</summary>
    /// <param name="result">The pending result.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static async Task<IResult> ToHttpResultAsync(this Task<Result> result, Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).ToHttpResult(statusCode);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then converts it as
    /// <see cref="ToHttpResult(Result, Func{IResult}, Func{Error, int})"/> does.
    /// </summary>
    /// <param name="result">The pending result.</param>
    /// <param name="onSuccess">Builds the success response.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static async Task<IResult> ToHttpResultAsync(
        this Task<Result> result,
        Func<IResult> onSuccess,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return (await result.ConfigureAwait(false)).ToHttpResult(onSuccess, statusCode);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then converts it as
    /// <see cref="ToHttpResult{TValue}(Result{TValue}, Func{Error, int})"/> does.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="result">The pending result.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static async Task<IResult> ToHttpResultAsync<TValue>(
        this Task<Result<TValue>> result,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).ToHttpResult(statusCode);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then converts it as
    /// <see cref="ToHttpResult{TValue}(Result{TValue}, Func{TValue, IResult}, Func{Error, int})"/> does.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="result">The pending result.</param>
    /// <param name="onSuccess">Builds the success response from the value.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static async Task<IResult> ToHttpResultAsync<TValue>(
        this Task<Result<TValue>> result,
        Func<TValue, IResult> onSuccess,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return (await result.ConfigureAwait(false)).ToHttpResult(onSuccess, statusCode);
    }

    private static ProblemHttpResult Problem(Error error, Func<Error, int>? statusCode) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: error.ToStatusCode(statusCode),
            extensions: [new(ErrorHttpExtensions.CodeExtension, error.Code)]);
}
