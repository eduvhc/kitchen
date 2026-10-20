namespace MailingKit.Sending;

/// <summary>
/// Attribute names and values. OpenTelemetry defines no email conventions, so everything except
/// <c>error.type</c> is under the <c>mailingkit.</c> prefix.
/// </summary>
internal static class MailingKitTags
{
    public const string SendActivityName = "send email";

    public const string Provider = "mailingkit.provider";
    public const string Outcome = "mailingkit.outcome";
    public const string RecipientCount = "mailingkit.email.recipient_count";
    public const string AttachmentCount = "mailingkit.email.attachment_count";
    public const string ProviderMessageId = "mailingkit.email.provider_message_id";
    public const string ErrorKind = "mailingkit.error.kind";
    public const string ErrorType = "error.type";

    public const string Sent = "sent";
    public const string Failed = "failed";

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
