using EmailService.Features.Emails.Contracts;

namespace EmailService.Features.Emails.SendEmail;

public record SendEmailResult(EmailResponse Email, bool Deduplicated);
