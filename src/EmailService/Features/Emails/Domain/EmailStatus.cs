namespace EmailService.Features.Emails.Domain;

public enum EmailStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Dead = 4,
    Cancelled = 5,
}
