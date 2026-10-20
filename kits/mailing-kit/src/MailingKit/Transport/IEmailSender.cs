using ResultingKit;

namespace MailingKit.Transport;

/// <summary>Sends email through a provider such as SMTP or an HTTP API.</summary>
public interface IEmailSender
{
    /// <summary>Sends one email.</summary>
    /// <param name="email">The email, with templates already rendered and recipients already validated.</param>
    /// <param name="ct">Cancels the send.</param>
    /// <returns>
    /// A <see cref="SendReceipt"/> when the provider accepts the email. On failure, an error of kind
    /// <see cref="ErrorKind.Transient"/> is retried by the inbox; any other kind sends the message straight
    /// to dead-letter.
    /// </returns>
    Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default);
}
