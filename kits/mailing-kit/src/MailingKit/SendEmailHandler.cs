using MailingKit.Domain;
using MailingKit.Options;
using MailingKit.Templates;
using MailingKit.Templating;
using MailingKit.Transport;
using MessagingKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MailingKit;

/// <summary>
/// Renders and sends one email. MessagingKit's inbox has already deduplicated the message and owns
/// the retry ladder, so this does the work and either succeeds or throws.
/// </summary>
/// <param name="templates">Null when no template store is configured; template requests then fail loudly.</param>
internal sealed class SendEmailHandler<TContext>(
    TContext db,
    IEmailSender sender,
    ITemplateRenderer renderer,
    TimeProvider clock,
    IOptions<MailingOptions> options,
    ITemplateStore? templates = null) : IMessageHandler<SendEmail>
    where TContext : DbContext
{
    private readonly EmailDefaultsOptions _defaults = options.Value.Defaults;

    public async Task HandleAsync(SendEmail message, MessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        var recipients = SendEmailValidator.Normalize(message.To);
        var cc = SendEmailValidator.Normalize(message.Cc);
        var bcc = SendEmailValidator.Normalize(message.Bcc);

        SendEmailValidator.ValidateRecipients(recipients, cc, bcc, _defaults);

        var content = await ResolveContentAsync(message, ct);

        SendEmailValidator.ValidateContent(content.Subject, content.Html, content.Text);

        var attachments = SendEmailValidator.MapAttachments(message.Attachments, _defaults);

        var outgoing = new OutgoingEmail
        {
            FromAddress = content.FromAddress,
            FromName = content.FromName,
            ReplyTo = message.ReplyTo ?? _defaults.ReplyTo,
            To = recipients,
            Cc = cc,
            Bcc = bcc,
            Subject = content.Subject,
            HtmlBody = content.Html,
            TextBody = content.Text,
            Attachments = attachments,
            Headers = new Dictionary<string, string>(message.Headers),
        };

        var result = await sender.SendAsync(outgoing, ct);

        await RecordAsync(message, context, outgoing, result, ct);

        if (result.Success)
        {
            return;
        }

        // Throwing hands the outcome back to the inbox, which owns retry and dead-lettering.
        throw new EmailSendException(result.Error ?? "Unknown send failure", result.IsPermanent);
    }

    private async Task RecordAsync(
        SendEmail message,
        MessageContext context,
        OutgoingEmail outgoing,
        SendResult result,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var logs = db.Set<EmailLog>();

        // A retried attempt updates its row rather than adding another: one row per message.
        var existing = await logs.FirstOrDefaultAsync(e => e.MessageId == context.MessageId, ct);

        if (existing is null)
        {
            existing = new EmailLog
            {
                MessageId = context.MessageId,
                Source = string.IsNullOrWhiteSpace(message.Source) ? null : message.Source,
                FromAddress = outgoing.FromAddress,
                FromName = outgoing.FromName,
                ReplyTo = outgoing.ReplyTo,
                To = outgoing.To,
                Cc = outgoing.Cc,
                Bcc = outgoing.Bcc,
                Subject = outgoing.Subject,
                TemplateKey = message.Template,
                CreatedAt = now,
            };

            logs.Add(existing);
        }

        existing.AttemptCount = context.AttemptCount;
        existing.Status = result.Success ? EmailStatus.Sent : EmailStatus.Failed;
        existing.SentAt = result.Success ? now : null;
        existing.ProviderMessageId = result.ProviderMessageId;
        existing.LastError = Truncate(result.Error);

        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? error) =>
        error is { Length: > 4000 } ? error[..4000] : error;

    private async Task<ResolvedContent> ResolveContentAsync(SendEmail message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Template))
        {
            return new ResolvedContent(
                message.Subject ?? string.Empty,
                message.Html,
                message.Text,
                message.From ?? _defaults.FromAddress,
                message.FromName ?? _defaults.FromName);
        }

        if (templates is null)
        {
            throw new ValidationException(
                $"Message names template '{message.Template}' but no template store is configured. "
                + "Call WithFileTemplates() or WithDatabaseTemplates() when registering MailingKit.");
        }

        var template = await templates.GetByKeyAsync(message.Template, ct)
            ?? throw new ValidationException($"Template '{message.Template}' was not found.");

        if (!template.IsActive)
        {
            throw new ValidationException($"Template '{message.Template}' is not active.");
        }

        var model = message.Model;

        return new ResolvedContent(
            message.Subject ?? renderer.Render(template.SubjectTemplate, model),
            message.Html ?? (template.HtmlTemplate is null ? null : renderer.Render(template.HtmlTemplate, model)),
            message.Text ?? (template.TextTemplate is null ? null : renderer.Render(template.TextTemplate, model)),
            message.From ?? template.FromAddress ?? _defaults.FromAddress,
            message.FromName ?? template.FromName ?? _defaults.FromName);
    }

    private sealed record ResolvedContent(string Subject, string? Html, string? Text, string FromAddress, string? FromName);
}
