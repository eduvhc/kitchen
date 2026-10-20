namespace ResultingKit;

/// <summary>
/// What kind of failure an <see cref="Error"/> is. Hosts map it to a status code; dispatchers use it
/// to decide whether to retry.
/// </summary>
public enum ErrorKind
{
    /// <summary>A failure with nothing more specific to say.</summary>
    Failure,

    /// <summary>The input was wrong. Retrying the same input cannot help.</summary>
    Validation,

    NotFound,

    Conflict,

    Unauthorized,

    Forbidden,

    /// <summary>
    /// Expected to pass on its own — a dropped connection, a busy server. The only kind worth retrying.
    /// </summary>
    Transient,
}
