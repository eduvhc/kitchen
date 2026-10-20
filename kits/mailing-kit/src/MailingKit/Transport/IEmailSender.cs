using ResultingKit;

namespace MailingKit.Transport;

public interface IEmailSender
{
    /// <summary>
    /// Sends one fully resolved email. Return a <see cref="ErrorKind.Transient"/> error for what retrying
    /// can fix; any other kind dead-letters the message instead of burning attempts.
    /// </summary>
    Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default);
}
