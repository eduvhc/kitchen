namespace MailingKit.Emails.SendEmail;

/// <param name="EmailId">Identifier of the staged (or previously staged) email.</param>
/// <param name="Deduplicated">True when an idempotency key matched an existing email and nothing new was staged.</param>
public record SendEmailResult(Guid EmailId, bool Deduplicated);
