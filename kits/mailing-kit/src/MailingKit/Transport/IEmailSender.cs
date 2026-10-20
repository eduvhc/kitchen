using ResultingKit;

namespace MailingKit.Transport;

public interface IEmailSender
{
    Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default);
}
