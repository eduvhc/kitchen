namespace ResultingKit;

public static class ResultTaskExtensions
{
    public static async Task<Result<TOut>> MapAsync<TValue, TOut>(
        this Task<Result<TValue>> result,
        Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).Map(map);
    }

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

    public static async Task<Result<TOut>> BindAsync<TValue, TOut>(
        this Task<Result<TValue>> result,
        Func<TValue, Task<Result<TOut>>> next)
    {
        ArgumentNullException.ThrowIfNull(result);

        return await (await result.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
    }

    public static async Task<TOut> MatchAsync<TValue, TOut>(
        this Task<Result<TValue>> result,
        Func<TValue, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (await result.ConfigureAwait(false)).Match(onSuccess, onFailure);
    }
}
