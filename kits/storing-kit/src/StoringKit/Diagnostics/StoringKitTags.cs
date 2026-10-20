namespace StoringKit;

/// <summary>
/// Attribute names and values. OpenTelemetry's object-storage conventions are AWS-specific and carry the key, which
/// StoringKit never records, so everything except <c>error.type</c> is under the <c>storingkit.</c> prefix.
/// </summary>
internal static class StoringKitTags
{
    public const string Store = "storingkit.store";
    public const string Provider = "storingkit.provider";
    public const string Operation = "storingkit.operation";
    public const string Outcome = "storingkit.outcome";
    public const string ErrorKind = "storingkit.error.kind";
    public const string ErrorType = "error.type";

    public const string ObjectSize = "storingkit.object.size";
    public const string ObjectExists = "storingkit.object.exists";
    public const string WriteCondition = "storingkit.write.condition";
    public const string ListedCount = "storingkit.list.object_count";

    public const string Success = "success";
    public const string Failure = "failure";

    public static string ToTagValue(ObjectStoreOperation operation) => operation switch
    {
        ObjectStoreOperation.Put => "put",
        ObjectStoreOperation.Get => "get",
        ObjectStoreOperation.GetProperties => "get_properties",
        ObjectStoreOperation.Exists => "exists",
        ObjectStoreOperation.Delete => "delete",
        ObjectStoreOperation.List => "list",
        ObjectStoreOperation.GetReadUrl => "get_read_url",
        _ => "other",
    };

    public static string ToTagValue(WriteCondition? condition) => condition switch
    {
        null => "none",
        { IsCreateOnly: true } => "if_not_exists",
        _ => "if_match",
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
