using System.Diagnostics.CodeAnalysis;

namespace ResultingKit;

/// <summary>
/// The outcome of an operation that either succeeds with a <typeparamref name="TValue"/> or fails with
/// an <see cref="Error"/>.
/// </summary>
/// <typeparam name="TValue">The type of the value carried on success.</typeparam>
/// <remarks>
/// Both a <typeparamref name="TValue"/> and an <see cref="Error"/> convert implicitly, so a method can
/// <c>return order;</c> or <c>return Error.NotFound(...)</c> directly. Derives from <see cref="Result"/>,
/// so it can be passed anywhere a plain result is expected.
/// </remarks>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory API.")]
public sealed class Result<TValue> : Result
{
    private readonly TValue _value;

    private Result(TValue value, Error? error) : base(error) => _value = value;

    /// <summary>The value of a successful result.</summary>
    /// <exception cref="InvalidOperationException">
    /// The result is a failure. Check <see cref="Result.IsSuccess"/> first, or use
    /// <see cref="TryGetValue"/> or <see cref="GetValueOrDefault"/>.
    /// </exception>
    public TValue Value => IsSuccess
        ? _value
        : throw new InvalidOperationException($"A failed result has no value. {Error}");

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The value. May be <see langword="null"/> if <typeparamref name="TValue"/> allows it.</param>
    public static Result<TValue> Success(TValue value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The reason for the failure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static new Result<TValue> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(default!, error);
    }

    /// <summary>Converts a value into a successful result.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Converts an <see cref="Error"/> into a failed result.</summary>
    /// <param name="error">The reason for the failure.</param>
    public static implicit operator Result<TValue>(Error error) => Failure(error);

    /// <summary>Gets the value without throwing.</summary>
    /// <param name="value">The value on success; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when the result is a success.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>Returns the value on success, or <paramref name="fallback"/> on failure.</summary>
    /// <param name="fallback">The value to return when the result is a failure.</param>
    public TValue GetValueOrDefault(TValue fallback) => IsSuccess ? _value : fallback;

    /// <summary>Runs one of two functions depending on the outcome and returns its value.</summary>
    /// <typeparam name="TOut">The type both functions return.</typeparam>
    /// <param name="onSuccess">Called with the value when the result is a success.</param>
    /// <param name="onFailure">Called with the error when the result is a failure.</param>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value) : onFailure(Error);
    }

    /// <summary>
    /// On success, transforms the value with <paramref name="map"/>. On failure, returns the same error
    /// without calling it.
    /// </summary>
    /// <typeparam name="TOut">The type of the transformed value.</typeparam>
    /// <param name="map">Transforms the value. Should not fail; use <see cref="Bind{TOut}"/> if it can.</param>
    public Result<TOut> Map<TOut>(Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? Result<TOut>.Success(map(_value)) : Result<TOut>.Failure(Error);
    }

    /// <summary>
    /// On success, passes the value to <paramref name="next"/> and returns its result. On failure,
    /// returns the same error without calling it.
    /// </summary>
    /// <typeparam name="TOut">The type of the next operation's value.</typeparam>
    /// <param name="next">The next operation, which may itself fail.</param>
    public Result<TOut> Bind<TOut>(Func<TValue, Result<TOut>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next(_value) : Result<TOut>.Failure(Error);
    }

    /// <summary>
    /// On success, passes the value to <paramref name="next"/> and returns its result. On failure,
    /// returns this result without calling it.
    /// </summary>
    /// <param name="next">The next operation, which produces no value and may itself fail.</param>
    public Result Bind(Func<TValue, Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next(_value) : this;
    }

    /// <summary>
    /// On failure, replaces the error with the one <paramref name="map"/> returns. On success, returns
    /// this result unchanged.
    /// </summary>
    /// <param name="map">Produces the replacement error.</param>
    public new Result<TValue> MapError(Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess ? this : Failure(map(Error));
    }

    /// <summary>Returns <c>"Success({Value})"</c> or <c>"Failure({Error})"</c>.</summary>
    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({Error})";
}
