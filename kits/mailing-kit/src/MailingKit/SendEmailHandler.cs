using MailingKit.Domain;
using MailingKit.Options;
using MailingKit.Templates;
using MailingKit.Templating;
using MailingKit.Transport;
using MessagingKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResultingKit;

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
    internal const string InterruptedError = "Interrupted: the attempt ended before the transport answered.";

    private readonly EmailDefaultsOptions _defaults = options.Value.Defaults;

    public async Task HandleAsync(SendEmail message, MessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        // A retried attempt reuses its row rather than adding another: one row per message.
        var log = await db.Set<EmailLog>().FirstOrDefaultAsync(e => e.MessageId == context.MessageId, ct);

        if (log is { Status: EmailStatus.Sent })
        {
            // The inbox is at-least-once: it reruns a handler whose success it failed to record.
            return;
        }

        var recipients = SendEmailValidator.Normalize(message.To);
        var cc = SendEmailValidator.Normalize(message.Cc);
        var bcc = SendEmailValidator.Normalize(message.Bcc);

        SendEmailValidator.ValidateRecipients(recipients, cc, bcc, _defaults);

        var attachments = SendEmailValidator.MapAttachments(message.Attachments, _defaults);

        log ??= await PinAsync(message, context, recipients, cc, bcc, ct);

        // Built from the pinned row, not re-rendered: a template edited between attempts would
        // otherwise change the payload, and a provider holding this idempotency key rejects that.
        var outgoing = new OutgoingEmail
        {
            IdempotencyKey = IdempotencyKeyFor(context.MessageId),
            FromAddress = log.FromAddress,
            FromName = log.FromName,
            ReplyTo = log.ReplyTo,
            To = recipients,
            Cc = cc,
            Bcc = bcc,
            Subject = log.Subject,
            HtmlBody = log.HtmlBody,
            TextBody = log.TextBody,
            Attachments = attachments,
            Headers = new Dictionary<string, string>(message.Headers),
        };

        var result = await sender.SendAsync(outgoing, ct);

        await RecordAsync(log, context, result, ct);

        if (result.IsSuccess)
        {
            return;
        }

        // Throwing hands the outcome back to the inbox, which owns retry and dead-lettering.
        throw new EmailSendException(result.Error.ToString(), !result.Error.IsTransient);
    }

    /// <summary>
    /// Renders the email and saves the result before the first send, so every later attempt sends the
    /// same content even if the process dies before the provider's answer is recorded.
    /// </summary>
    private async Task<EmailLog> PinAsync(
        SendEmail message,
        MessageContext context,
        List<string> recipients,
        List<string> cc,
        List<string> bcc,
        CancellationToken ct)
    {
        var content = await ResolveContentAsync(message, ct);

        SendEmailValidator.ValidateContent(content.Subject, content.Html, content.Text);

        var log = new EmailLog
        {
            MessageId = context.MessageId,
            Source = string.IsNullOrWhiteSpace(message.Source) ? null : message.Source,
            FromAddress = content.FromAddress,
            FromName = content.FromName,
            ReplyTo = message.ReplyTo ?? _defaults.ReplyTo,
            To = recipients,
            Cc = cc,
            Bcc = bcc,
            Subject = content.Subject,
            HtmlBody = content.Html,
            TextBody = content.Text,
            TemplateKey = message.Template,
            CreatedAt = clock.GetUtcNow(),

            // Overwritten by the outcome; left standing only if the attempt never reports back.
            AttemptCount = context.AttemptCount,
            Status = EmailStatus.Failed,
            LastError = InterruptedError,
        };

        db.Set<EmailLog>().Add(log);
        await db.SaveChangesAsync(ct);
        return log;
    }

    private async Task RecordAsync(EmailLog log, MessageContext context, Result<SendReceipt> result, CancellationToken ct)
    {
        log.AttemptCount = context.AttemptCount;
        log.Status = result.IsSuccess ? EmailStatus.Sent : EmailStatus.Failed;
        log.SentAt = result.IsSuccess ? clock.GetUtcNow() : null;
        log.ProviderMessageId = result.IsSuccess ? result.Value.ProviderMessageId : null;
        log.LastError = Truncate(result.Error?.ToString());

        if (result.IsSuccess)
        {
            log.HtmlBody = null;
            log.TextBody = null;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The inbox redelivers a message under the same id, so every retry derives the same key.
    /// </summary>
    internal static string IdempotencyKeyFor(Guid messageId) => $"mailingkit-{messageId:N}";

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
