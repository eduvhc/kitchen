namespace MailingKit.Transport;

/// <summary>What a provider hands back for an accepted email.</summary>
/// <param name="ProviderMessageId">The provider's id for the message, when it gives one; recorded on the send log.</param>
public sealed record SendReceipt(string? ProviderMessageId = null);
