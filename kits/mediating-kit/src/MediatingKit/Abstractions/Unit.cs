namespace MediatingKit;

/// <summary>
/// The response of a request that has none. Pipeline behaviors see an <see cref="IRequest"/> as an
/// <see cref="IRequest{TResponse}"/> of <see cref="Unit"/>, so one behavior covers both kinds of request.
/// </summary>
public readonly record struct Unit
{
    /// <summary>The only value.</summary>
    public static Unit Value => default;

    /// <inheritdoc />
    public override string ToString() => "()";
}
