namespace MailingKit;

/// <summary>
/// Thrown when a transport refuses an email. The inbox catches it and applies its retry ladder.
/// </summary>
/// <remarks>
/// <see cref="IsPermanent"/> records that retrying cannot help — a malformed address, a 5xx reply.
/// Whether the inbox acts on that is its decision; recording it here keeps the reason in the message.
/// </remarks>
public sealed class EmailSendException(string message, bool isPermanent) : Exception(message)
{
    public bool IsPermanent { get; } = isPermanent;
}
