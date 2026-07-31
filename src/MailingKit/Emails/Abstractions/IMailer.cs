using MailingKit.Emails.SendEmail;

namespace MailingKit.Emails.Abstractions;

/// <summary>
/// The product-facing entry point. Validates and renders the request, then stages the email on the
/// host's <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.
/// </summary>
/// <remarks>
/// Nothing is written until the host calls <c>SaveChangesAsync</c>, so an email staged inside the
/// caller's transaction commits with it or not at all.
/// </remarks>
public interface IMailer
{
    Task<SendEmailResult> SendAsync(SendEmailRequest request, CancellationToken ct = default);
}
