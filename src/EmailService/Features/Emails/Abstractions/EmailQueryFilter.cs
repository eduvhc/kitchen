using EmailService.Features.Emails.Domain;

namespace EmailService.Features.Emails.Abstractions;

public record EmailQueryFilter(
    EmailStatus? Status = null,
    string? Recipient = null,
    string? TemplateKey = null,
    string? Source = null,
    int Limit = 50,
    int Offset = 0);
