namespace MailingKit.Sending;

/// <summary>Confirms that a provider accepted an email.</summary>
/// <param name="ProviderMessageId">
/// The provider's id for the email, if it returns one. Stored on the email log.
/// </param>
public sealed record SendReceipt(string? ProviderMessageId = null);
