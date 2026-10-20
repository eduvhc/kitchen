using Microsoft.AspNetCore.Mvc;

namespace ResultingKit.AspNetCore;

/// <summary>
/// Turns a <see cref="Result"/> into an <see cref="IActionResult"/> for controllers. A failure becomes an
/// <see cref="ErrorActionResult"/>, an RFC 9457 problem built by MVC's <c>ProblemDetailsFactory</c>.
/// </summary>
public static class ResultActionResultExtensions
{
    /// <summary>Returns 204 No Content on success, or a problem response on failure.</summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="statusCode">
    /// Picks the status code for a failure. <see langword="null"/> uses
    /// <see cref="ErrorHttpExtensions.ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static IActionResult ToActionResult(this Result result, Func<Error, int>? statusCode = null) =>
        result.ToActionResult(static () => new NoContentResult(), statusCode);

    /// <summary>Returns what <paramref name="onSuccess"/> builds on success, or a problem response on failure.</summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="onSuccess">Builds the success response, such as <c>() =&gt; new AcceptedResult()</c>.</param>
    /// <param name="statusCode">
    /// Picks the status code for a failure. <see langword="null"/> uses
    /// <see cref="ErrorHttpExtensions.ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static IActionResult ToActionResult(
        this Result result,
        Func<IActionResult> onSuccess,
        Func<Error, int>? statusCode = null)
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
    public static IActionResult ToActionResult<TValue>(this Result<TValue> result, Func<Error, int>? statusCode = null) =>
        result.ToActionResult(static value => new OkObjectResult(value), statusCode);

    /// <summary>
    /// Returns what <paramref name="onSuccess"/> builds from the value on success, or a problem response
    /// on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="onSuccess">
    /// Builds the success response from the value, such as
    /// <c>order =&gt; CreatedAtAction(nameof(Get), new { id = order.Id }, order)</c>.
    /// </param>
    /// <param name="statusCode">
    /// Picks the status code for a failure. <see langword="null"/> uses
    /// <see cref="ErrorHttpExtensions.ToStatusCode(Error)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static IActionResult ToActionResult<TValue>(
        this Result<TValue> result,
        Func<TValue, IActionResult> onSuccess,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error, statusCode);
    }

    /// <summary>Awaits <paramref name="result"/>, then converts it as <see cref="ToActionResult(Result, Func{Error, int})"/> does.</summary>
    /// <param name="result">The pending result.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static async Task<IActionResult> ToActionResultAsync(this Task<Result> result, Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).ToActionResult(statusCode);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then converts it as
    /// <see cref="ToActionResult(Result, Func{IActionResult}, Func{Error, int})"/> does.
    /// </summary>
    /// <param name="result">The pending result.</param>
    /// <param name="onSuccess">Builds the success response.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static async Task<IActionResult> ToActionResultAsync(
        this Task<Result> result,
        Func<IActionResult> onSuccess,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return (await result.ConfigureAwait(false)).ToActionResult(onSuccess, statusCode);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then converts it as
    /// <see cref="ToActionResult{TValue}(Result{TValue}, Func{Error, int})"/> does.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="result">The pending result.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static async Task<IActionResult> ToActionResultAsync<TValue>(
        this Task<Result<TValue>> result,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).ToActionResult(statusCode);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then converts it as
    /// <see cref="ToActionResult{TValue}(Result{TValue}, Func{TValue, IActionResult}, Func{Error, int})"/> does.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="result">The pending result.</param>
    /// <param name="onSuccess">Builds the success response from the value.</param>
    /// <param name="statusCode">Picks the status code for a failure. <see langword="null"/> uses the default mapping.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> or <paramref name="onSuccess"/> is <see langword="null"/>.
    /// </exception>
    public static async Task<IActionResult> ToActionResultAsync<TValue>(
        this Task<Result<TValue>> result,
        Func<TValue, IActionResult> onSuccess,
        Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return (await result.ConfigureAwait(false)).ToActionResult(onSuccess, statusCode);
    }

    private static ErrorActionResult Problem(Error error, Func<Error, int>? statusCode) =>
        new(error, error.ToStatusCode(statusCode));
}
