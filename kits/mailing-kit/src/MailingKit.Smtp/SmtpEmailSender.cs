using MailingKit.Domain;
using Microsoft.Extensions.Logging;
using MailingKit.Transport;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MailingKit.Smtp;

public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task<SendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        MimeMessage mime;
        try
        {
            mime = Build(email);
        }
        catch (ParseException ex)
        {
            return SendResult.Permanent($"Malformed address: {ex.Message}");
        }

        using var client = new SmtpClient
        {
            Timeout = _options.TimeoutSeconds * 1000,
        };

        if (_options.AcceptAllCertificates)
        {
            // Local development only, and documented as such. A self-signed Mailpit or MailHog
            // container has no certificate worth validating.
#pragma warning disable CA5359 // Do Not Disable Certificate Validation
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        }

        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, _options.Security, ct);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, ct);
            }

            var response = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Sent email to {RecipientCount} recipients", email.To.Count);
            }
            return SendResult.Ok(mime.MessageId ?? response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SmtpCommandException ex) when (IsPermanent(ex))
        {
            logger.LogWarning(ex, "Permanent SMTP failure for {Subject}", email.Subject);
            return SendResult.Permanent($"{ex.ErrorCode}/{ex.StatusCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Transient SMTP failure for {Subject}", email.Subject);
            return SendResult.Transient($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsPermanent(SmtpCommandException ex) =>
        (int)ex.StatusCode >= 500 && (int)ex.StatusCode < 600;

    private static MimeMessage Build(OutgoingEmail message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName ?? string.Empty, message.FromAddress));

        foreach (var address in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(address));
        }

        foreach (var address in message.Cc)
        {
            mime.Cc.Add(MailboxAddress.Parse(address));
        }

        foreach (var address in message.Bcc)
        {
            mime.Bcc.Add(MailboxAddress.Parse(address));
        }

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        }

        mime.Subject = message.Subject;

        foreach (var (name, value) in message.Headers)
        {
            mime.Headers.Add(name, value);
        }

        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        };

        foreach (var attachment in message.Attachments)
        {
            var bytes = Convert.FromBase64String(attachment.Content);
            var contentType = ContentType.Parse(attachment.ContentType);

            if (!string.IsNullOrWhiteSpace(attachment.ContentId))
            {
                var linked = builder.LinkedResources.Add(attachment.FileName, bytes, contentType);
                linked.ContentId = attachment.ContentId;
            }
            else
            {
                builder.Attachments.Add(attachment.FileName, bytes, contentType);
            }
        }

        if (builder.HtmlBody is null && builder.TextBody is null)
        {
            builder.TextBody = string.Empty;
        }

        mime.Body = builder.ToMessageBody();
        return mime;
    }
}
