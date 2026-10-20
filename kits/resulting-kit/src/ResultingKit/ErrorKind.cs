namespace ResultingKit;

/// <summary>
/// Classifies an <see cref="Error"/>. Hosts typically map it to a status code; retry loops use it
/// through <see cref="Error.IsTransient"/>.
/// </summary>
public enum ErrorKind
{
    /// <summary>A failure with no more specific kind. Not retried.</summary>
    Failure,

    /// <summary>The input was invalid. Not retried.</summary>
    Validation,

    /// <summary>The requested resource does not exist. Not retried.</summary>
    NotFound,

    /// <summary>The operation conflicts with the current state. Not retried.</summary>
    Conflict,

    /// <summary>The caller is not authenticated. Not retried.</summary>
    Unauthorized,

    /// <summary>The caller is authenticated but not allowed. Not retried.</summary>
    Forbidden,

    /// <summary>A temporary failure, such as a timeout or an unreachable server. The only kind that is retried.</summary>
    Transient,
}
