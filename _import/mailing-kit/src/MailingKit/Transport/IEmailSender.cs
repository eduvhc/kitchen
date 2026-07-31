
namespace MailingKit.Transport;

public interface IEmailSender
{
    Task<SendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default);
}
