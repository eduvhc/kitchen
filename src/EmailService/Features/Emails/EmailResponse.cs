namespace EmailService.Features.Emails;

public record EmailResponse(
    Guid Id,
    EmailStatus Status,
    List<string> To,
    string Subject,
    string? TemplateKey,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? SentAt,
    string? LastError,
    string? ProviderMessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static EmailResponse FromEntity(EmailMessage m) => new(
        m.Id,
        m.Status,
        m.To,
        m.Subject,
        m.TemplateKey,
        m.AttemptCount,
        m.MaxAttempts,
        m.ScheduledAt,
        m.SentAt,
        m.LastError,
        m.ProviderMessageId,
        m.CreatedAt,
        m.UpdatedAt);
}
