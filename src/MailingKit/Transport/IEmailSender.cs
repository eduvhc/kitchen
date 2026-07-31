using MailingKit.Emails.Domain;

namespace MailingKit.Transport;

public interface IEmailSender
{
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
