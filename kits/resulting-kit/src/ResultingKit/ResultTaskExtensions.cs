namespace ResultingKit;

/// <summary>
/// Async versions of <see cref="Result{TValue}"/>'s combinators, so a chain of async steps can be written
/// without awaiting each one.
/// </summary>
public static class ResultTaskExtensions
{
    /// <summary>
    /// Awaits <paramref name="result"/>, then transforms its value with <paramref name="map"/> on success.
    /// </summary>
    /// <typeparam name="TValue">The type of the awaited value.</typeparam>
    /// <typeparam name="TOut">The type of the transformed value.</typeparam>
    /// <param name="result">The pending result.</param>
    /// <param name="map">Transforms the value. Not called on failure.</param>
    public static async Task<Result<TOut>> MapAsync<TValue, TOut>(
        this Task<Result<TValue>> result,
        Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).Map(map);
    }

    /// <summary>
    /// On success, passes the value to the async <paramref name="next"/> and returns its result. On
    /// failure, returns the same error without calling it.
    /// </summary>
    /// <typeparam name="TValue">The type of the current value.</typeparam>
    /// <typeparam name="TOut">The type of the next operation's value.</typeparam>
    /// <param name="result">The current result.</param>
    /// <param name="next">The next async operation, which may itself fail.</param>
    public static async Task<Result<TOut>> BindAsync<TValue, TOut>(
        this Result<TValue> result,
        Func<TValue, Task<Result<TOut>>> next)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(next);

        return result.IsSuccess
            ? await next(result.Value).ConfigureAwait(false)
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then on success passes the value to the async
    /// <paramref name="next"/> and returns its result.
    /// </summary>
    /// <typeparam name="TValue">The type of the awaited value.</typeparam>
    /// <typeparam name="TOut">The type of the next operation's value.</typeparam>
    /// <param name="result">The pending result.</param>
    /// <param name="next">The next async operation, which may itself fail. Not called on failure.</param>
    public static async Task<Result<TOut>> BindAsync<TValue, TOut>(
        this Task<Result<TValue>> result,
        Func<TValue, Task<Result<TOut>>> next)
    {
        ArgumentNullException.ThrowIfNull(result);

        return await (await result.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
    }

    /// <summary>
    /// Awaits <paramref name="result"/>, then runs one of two functions depending on the outcome and
    /// returns its value.
    /// </summary>
    /// <typeparam name="TValue">The type of the awaited value.</typeparam>
    /// <typeparam name="TOut">The type both functions return.</typeparam>
    /// <param name="result">The pending result.</param>
    /// <param name="onSuccess">Called with the value when the result is a success.</param>
    /// <param name="onFailure">Called with the error when the result is a failure.</param>
    public static async Task<TOut> MatchAsync<TValue, TOut>(
        this Task<Result<TValue>> result,
        Func<TValue, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).Match(onSuccess, onFailure);
    }
}
