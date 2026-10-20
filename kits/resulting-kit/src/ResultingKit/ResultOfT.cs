using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory API.")]
public sealed class Result<TValue> : Result
{
    private readonly TValue _value;

    private Result(TValue value, Error? error) : base(error) => _value = value;

    public TValue Value => IsSuccess
        ? _value
        : throw new InvalidOperationException($"A failed result has no value. {Error}");

    public static Result<TValue> Success(TValue value) => new(value, null);

    public static new Result<TValue> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(default!, error);
    }

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure(error);

    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess;
    }

    public TValue GetValueOrDefault(TValue fallback) => IsSuccess ? _value : fallback;

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value) : onFailure(Error);
    }

    public Result<TOut> Map<TOut>(Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? Result<TOut>.Success(map(_value)) : Result<TOut>.Failure(Error);
    }

    public Result<TOut> Bind<TOut>(Func<TValue, Result<TOut>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next(_value) : Result<TOut>.Failure(Error);
    }

    public Result Bind(Func<TValue, Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next(_value) : this;
    }

    public new Result<TValue> MapError(Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? this : Failure(map(Error));
    }

    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({Error})";
}
