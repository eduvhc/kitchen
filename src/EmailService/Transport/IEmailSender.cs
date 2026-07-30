using EmailService.Features.Emails;

namespace EmailService.Transport;

public interface IEmailSender
{
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public readonly record struct SendResult(
    bool Success,
    string? ProviderMessageId = null,
    string? Error = null,
    bool IsPermanent = false)
{
    public static SendResult Ok(string? providerMessageId = null) => new(true, providerMessageId);

    public static SendResult Transient(string error) => new(false, Error: error);

    public static SendResult Permanent(string error) => new(false, Error: error, IsPermanent: true);
}
