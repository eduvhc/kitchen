namespace MailingKit.Domain;

/// <summary>
/// Terminal outcomes only. Anything still in flight lives in MessagingKit's inbox, not here.
/// </summary>
public enum EmailStatus
{
    Sent = 0,

    /// <summary>The transport refused it permanently, or attempts ran out.</summary>
    Failed = 1,
}
