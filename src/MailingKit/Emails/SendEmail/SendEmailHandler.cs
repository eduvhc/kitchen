using MailingKit.Emails.Abstractions;
using MailingKit.Emails.Domain;
using MailingKit.Options;
using MailingKit.Templates.Abstractions;
using MailingKit.Templating;
using Microsoft.Extensions.Options;

namespace MailingKit.Emails.SendEmail;

/// <param name="templates">Null when no template store is configured; template requests then fail loudly.</param>
internal sealed class SendEmailHandler(
    IEmailQueue queue,
    ITemplateRenderer renderer,
    TimeProvider clock,
    IOptions<MailingKitOptions> options,
    ITemplateStore? templates = null) : IMailer
{
    private readonly EmailDefaultsOptions _defaults = options.Value.Defaults;

    public async Task<SendEmailResult> SendAsync(SendEmailRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await queue.FindByIdempotencyKeyAsync(request.IdempotencyKey, ct);
            if (existing is not null)
            {
                return new SendEmailResult(existing.Id, true);
            }
        }

        var recipients = SendEmailValidator.Normalize(request.To);
        var cc = SendEmailValidator.Normalize(request.Cc);
        var bcc = SendEmailValidator.Normalize(request.Bcc);

        SendEmailValidator.ValidateRecipients(recipients, cc, bcc, _defaults);
        SendEmailValidator.ValidateMaxAttempts(request.MaxAttempts);

        var content = await ResolveContentAsync(request, ct);

        SendEmailValidator.ValidateContent(content.Subject, content.Html, content.Text);

        var attachments = SendEmailValidator.MapAttachments(request.Attachments, _defaults);

        var now = clock.GetUtcNow();
        var message = new EmailMessage
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey,
            Source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source,
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

        queue.Enqueue(message);
        return new SendEmailResult(message.Id, false);
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

        if (templates is null)
        {
            throw new ValidationException(
                $"Request names template '{request.Template}' but no template store is configured. " +
                "Call Templates.UseFiles() or Templates.UseDatabase() when registering MailingKit.");
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
