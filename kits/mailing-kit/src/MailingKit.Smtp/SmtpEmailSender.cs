using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using MailingKit.Sending;
using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using ResultingKit;

namespace MailingKit.Smtp;

public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private static readonly SearchValues<char> AtomChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!#$%&'*+-/=?^_`{|}~.");

    private readonly SmtpOptions _options = options.Value;

    public async Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        MimeMessage mime;
        try
        {
            mime = Build(email);
        }
        catch (ParseException ex)
        {
            return Error.Validation("smtp.malformed_address", $"Malformed address: {ex.Message}");
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
            return new SendReceipt(mime.MessageId ?? response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SmtpCommandException ex) when (IsPermanent(ex))
        {
            logger.LogWarning(ex, "Permanent SMTP failure for {Subject}", email.Subject);
            return Error.Failure("smtp.rejected", $"{ex.ErrorCode}/{ex.StatusCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Transient SMTP failure for {Subject}", email.Subject);
            return Error.Transient("smtp.unavailable", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>key@domain</c>, with the key used as-is when it is a valid <c>dot-atom</c> and hashed otherwise,
    /// so any key yields a well-formed id.
    /// </summary>
    internal static string DeterministicMessageId(string key, string domain)
    {
        var left = IsDotAtom(key)
            ? key
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        return $"{left}@{(string.IsNullOrWhiteSpace(domain) ? "localhost" : domain)}";
    }

    private static bool IsDotAtom(string value) =>
        value.Length > 0
        && !value.AsSpan().ContainsAnyExcept(AtomChars)
        && value[0] != '.' && value[^1] != '.'
        && !value.Contains("..", StringComparison.Ordinal);

    private static bool HasHeader(Dictionary<string, string> headers, string name) =>
        headers.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsPermanent(SmtpCommandException ex) =>
        (int)ex.StatusCode >= 500 && (int)ex.StatusCode < 600;

    internal static MimeMessage Build(OutgoingEmail message)
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

        // SMTP has no idempotency handshake. A Message-ID that repeats across retries is the closest
        // thing: receiving systems that deduplicate on it drop the second copy, and it ties the
        // delivered email back to its inbox message.
        if (!string.IsNullOrWhiteSpace(message.IdempotencyKey) && !HasHeader(message.Headers, "Message-ID"))
        {
            mime.MessageId = DeterministicMessageId(message.IdempotencyKey, mime.From.Mailboxes.First().Domain);
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
