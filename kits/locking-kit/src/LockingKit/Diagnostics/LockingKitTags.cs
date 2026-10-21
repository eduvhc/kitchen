namespace LockingKit;

/// <summary>
/// Attribute names and values. OpenTelemetry has no conventions for locks, so everything except <c>error.type</c> is
/// under the <c>lockingkit.</c> prefix. Keys are never recorded: they routinely carry ids.
/// </summary>
internal static class LockingKitTags
{
    public const string Lock = "lockingkit.lock";
    public const string Provider = "lockingkit.provider";
    public const string Operation = "lockingkit.operation";
    public const string Outcome = "lockingkit.outcome";
    public const string ErrorKind = "lockingkit.error.kind";
    public const string ErrorType = "error.type";
    public const string Attempts = "lockingkit.acquire.attempts";
    public const string LossReason = "lockingkit.loss.reason";
    public const string FencingToken = "lockingkit.fencing_token";

    public const string TryAcquire = "try_acquire";
    public const string Acquire = "acquire";
    public const string Release = "release";

    public const string Acquired = "acquired";
    public const string Busy = "busy";
    public const string TimedOut = "timeout";
    public const string Success = "success";
    public const string Failure = "failure";

    public static string ToTagValue(LockLossReason reason) => reason switch
    {
        LockLossReason.Expired => "expired",
        LockLossReason.Revoked => "revoked",
        LockLossReason.ConnectionLost => "connection_lost",
        _ => "other",
    };

    public static string ToTagValue(ResultingKit.ErrorKind kind) => kind switch
    {
        ResultingKit.ErrorKind.Failure => "failure",
        ResultingKit.ErrorKind.Validation => "validation",
        ResultingKit.ErrorKind.NotFound => "not_found",
        ResultingKit.ErrorKind.Conflict => "conflict",
        ResultingKit.ErrorKind.Unauthorized => "unauthorized",
        ResultingKit.ErrorKind.Forbidden => "forbidden",
        ResultingKit.ErrorKind.Transient => "transient",
        _ => "other",
    };
}
