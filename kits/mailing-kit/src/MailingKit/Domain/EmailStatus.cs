namespace MailingKit.Domain;

/// <summary>
/// The outcome of the latest attempt. Retries and dead-lettering live in MessagingKit's inbox, not here.
/// </summary>
public enum EmailStatus
{
    Sent = 0,

    /// <summary>
    /// The last attempt did not succeed: the transport refused it, or the attempt was interrupted
    /// before the transport answered. The inbox may still be retrying.
    /// </summary>
    Failed = 1,
}
