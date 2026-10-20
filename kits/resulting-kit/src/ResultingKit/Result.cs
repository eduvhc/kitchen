using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

/// <summary>
/// The outcome of an operation that can fail in an expected way. Exceptions stay for bugs and broken
/// wiring; a result is for failures the caller is meant to handle.
/// </summary>
public class Result
{
    private static readonly Result s_success = new(null);

    private protected Result(Error? error) => Error = error;

    /// <summary>Null exactly when <see cref="IsSuccess"/> is true.</summary>
    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    public static Result Success() => s_success;

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    public Result<TOut> Map<TOut>(Func<TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? Result<TOut>.Success(map()) : Result<TOut>.Failure(Error);
    }

    public Result Bind(Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next() : this;
    }

    public Result MapError(Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? this : Failure(map(Error));
    }

    public override string ToString() => IsSuccess ? "Success" : $"Failure({Error})";
}
