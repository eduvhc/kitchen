using EmailService.Common;
using EmailService.Features.Templates;
using EmailService.Options;
using EmailService.Templating;
using Microsoft.Extensions.Options;

namespace EmailService.Features.Emails.SendEmail;

public class SendEmailHandler(
    IEmailQueue queue,
    ITemplateStore templates,
    ITemplateRenderer renderer,
    TimeProvider clock,
    IOptions<EmailDefaultsOptions> options)
{
    private readonly EmailDefaultsOptions _defaults = options.Value;

    public async Task<SendEmailResult> HandleAsync(
        SendEmailRequest request,
        string? source,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await queue.FindByIdempotencyKeyAsync(request.IdempotencyKey, ct);
            if (existing is not null)
            {
                return new SendEmailResult(EmailResponse.FromEntity(existing), true);
            }
        }

        var recipients = SendEmailValidator.Normalize(request.To);
        var cc = SendEmailValidator.Normalize(request.Cc);
        var bcc = SendEmailValidator.Normalize(request.Bcc);

        SendEmailValidator.ValidateRecipients(recipients, cc, bcc, _defaults);

        var content = await ResolveContentAsync(request, ct);

        SendEmailValidator.ValidateContent(content.Subject, content.Html, content.Text);

        var attachments = SendEmailValidator.MapAttachments(request.Attachments, _defaults);

        var now = clock.GetUtcNow();
        var message = new EmailMessage
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey,
            Source = source,
            FromAddress = content.FromAddress,
            FromName = content.FromName,
            ReplyTo = request.ReplyTo ?? _defaults.ReplyTo,
            To = recipients,
            Cc = cc,
            Bcc = bcc,
            Subject = content.Subject,
            HtmlBody = content.Html,
            TextBody = content.Text,
            Attachments = attachments,
            Headers = new Dictionary<string, string>(request.Headers),
            TemplateKey = request.Template,
            Status = EmailStatus.Queued,
            MaxAttempts = request.MaxAttempts ?? _defaults.MaxAttempts,
            ScheduledAt = request.SendAt ?? now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var saved = await queue.EnqueueAsync(message, ct);
        return new SendEmailResult(EmailResponse.FromEntity(saved), false);
    }

    private async Task<ResolvedContent> ResolveContentAsync(SendEmailRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Template))
        {
            return new ResolvedContent(
                request.Subject ?? string.Empty,
                request.Html,
                request.Text,
                request.From ?? _defaults.FromAddress,
                request.FromName ?? _defaults.FromName);
        }

        var template = await templates.GetByKeyAsync(request.Template, ct)
            ?? throw new ValidationException($"Template '{request.Template}' was not found.");

        if (!template.IsActive)
        {
            throw new ValidationException($"Template '{request.Template}' is not active.");
        }

        var model = request.Model;

        return new ResolvedContent(
            request.Subject ?? renderer.Render(template.SubjectTemplate, model),
            request.Html ?? (template.HtmlTemplate is null ? null : renderer.Render(template.HtmlTemplate, model)),
            request.Text ?? (template.TextTemplate is null ? null : renderer.Render(template.TextTemplate, model)),
            request.From ?? template.FromAddress ?? _defaults.FromAddress,
            request.FromName ?? template.FromName ?? _defaults.FromName);
    }

    private record ResolvedContent(string Subject, string? Html, string? Text, string FromAddress, string? FromName);
}

public record SendEmailResult(EmailResponse Email, bool Deduplicated);
