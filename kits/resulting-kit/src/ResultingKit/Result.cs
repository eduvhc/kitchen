using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

/// <summary>
/// The outcome of an operation that either succeeds or fails with an <see cref="ResultingKit.Error"/>.
/// Use <see cref="Result{TValue}"/> when a success carries a value.
/// </summary>
/// <remarks>
/// An <see cref="ResultingKit.Error"/> converts implicitly, so a method returning <see cref="Result"/>
/// can <c>return Error.NotFound(...)</c> directly.
/// </remarks>
public class Result
{
    private static readonly Result s_success = new(null);

    private protected Result(Error? error) => Error = error;

    /// <summary>The reason for the failure, or <see langword="null"/> when the result is a success.</summary>
    public Error? Error { get; }

    /// <summary>
    /// <see langword="true"/> when the operation succeeded. When <see langword="false"/>, the compiler
    /// knows <see cref="Error"/> is not null.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>
    /// <see langword="true"/> when the operation failed. When <see langword="true"/>, the compiler knows
    /// <see cref="Error"/> is not null.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    /// <summary>Returns a successful result. The same instance is returned on every call.</summary>
    public static Result Success() => s_success;

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The reason for the failure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="value">The value. May be <see langword="null"/> if <typeparamref name="TValue"/> allows it.</param>
    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    /// <summary>Creates a failed result of type <see cref="Result{TValue}"/>.</summary>
    /// <typeparam name="TValue">The type the result would have carried on success.</typeparam>
    /// <param name="error">The reason for the failure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    /// <summary>Converts an <see cref="ResultingKit.Error"/> into a failed result.</summary>
    /// <param name="error">The reason for the failure.</param>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Runs one of two functions depending on the outcome and returns its value.</summary>
    /// <typeparam name="TOut">The type both functions return.</typeparam>
    /// <param name="onSuccess">Called when the result is a success.</param>
    /// <param name="onFailure">Called with the error when the result is a failure.</param>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    /// <summary>
    /// On success, produces a value with <paramref name="map"/>. On failure, returns the same error
    /// without calling it.
    /// </summary>
    /// <typeparam name="TOut">The type of the produced value.</typeparam>
    /// <param name="map">Produces the value for a successful result.</param>
    public Result<TOut> Map<TOut>(Func<TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? Result<TOut>.Success(map()) : Result<TOut>.Failure(Error);
    }

    /// <summary>
    /// On success, runs <paramref name="next"/> and returns its result. On failure, returns this result
    /// without calling it.
    /// </summary>
    /// <param name="next">The next operation, which may itself fail.</param>
    public Result Bind(Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next() : this;
    }

    /// <summary>
    /// On failure, replaces the error with the one <paramref name="map"/> returns. On success, returns
    /// this result unchanged.
    /// </summary>
    /// <param name="map">Produces the replacement error.</param>
    public Result MapError(Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? this : Failure(map(Error));
    }

    /// <summary>Returns <c>"Success"</c> or <c>"Failure({Error})"</c>.</summary>
    public override string ToString() => IsSuccess ? "Success" : $"Failure({Error})";
}
