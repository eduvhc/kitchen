namespace MediatingKit;

/// <summary>Attribute names and values. Everything except <c>error.type</c> is under the <c>mediatingkit.</c> prefix.</summary>
internal static class MediatingKitTags
{
    public const string Operation = "mediatingkit.operation";
    public const string MessageType = "mediatingkit.message.type";
    public const string Outcome = "mediatingkit.outcome";
    public const string ErrorType = "error.type";

    public const string Send = "send";
    public const string Publish = "publish";

    public const string Success = "success";
    public const string Failure = "failure";
}
