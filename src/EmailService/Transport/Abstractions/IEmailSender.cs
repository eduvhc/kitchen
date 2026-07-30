using EmailService.Features.Emails.Domain;

namespace EmailService.Transport.Abstractions;

public interface IEmailSender
{
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
