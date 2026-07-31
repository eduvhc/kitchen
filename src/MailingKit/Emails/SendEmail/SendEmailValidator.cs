using MailingKit;
using MailingKit.Emails.Domain;
using MailingKit.Options;
using System.Net.Mail;

namespace MailingKit.Emails.SendEmail;

internal static class SendEmailValidator
{
    public static void ValidateMaxAttempts(int? maxAttempts)
    {
        if (maxAttempts is { } value && value is < 1 or > 20)
        {
            throw new ValidationException($"MaxAttempts must be between 1 and 20, but was {value}.");
        }
    }

    public static List<string> Normalize(IEnumerable<string> addresses) =>
        addresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void ValidateRecipients(
        List<string> to,
        List<string> cc,
        List<string> bcc,
        EmailDefaultsOptions defaults)
    {
        if (to.Count == 0)
        {
            throw new ValidationException("At least one recipient in 'to' is required.");
        }

        var total = to.Count + cc.Count + bcc.Count;
        if (total > defaults.MaxRecipients)
        {
            throw new ValidationException($"Too many recipients ({total}); the limit is {defaults.MaxRecipients}.");
        }

        foreach (var address in to.Concat(cc).Concat(bcc))
        {
            ValidateAddress(address, defaults);
        }
    }

    public static void ValidateContent(string? subject, string? html, string? text)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ValidationException("A subject is required.");
        }

        if (string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(text))
        {
            throw new ValidationException("An email needs an 'html' or 'text' body.");
        }
    }

    public static List<EmailAttachment> MapAttachments(List<AttachmentDto> attachments, EmailDefaultsOptions defaults)
    {
        var mapped = new List<EmailAttachment>(attachments.Count);
        var totalBytes = 0L;

        foreach (var attachment in attachments)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(attachment.Content);
            }
            catch (FormatException)
            {
                throw new ValidationException($"Attachment '{attachment.FileName}' is not valid base64.");
            }

            totalBytes += bytes.LongLength;
            if (totalBytes > defaults.MaxAttachmentBytes)
            {
                throw new ValidationException($"Attachments exceed the {defaults.MaxAttachmentBytes} byte limit.");
            }

            mapped.Add(new EmailAttachment
            {
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                Content = attachment.Content,
                ContentId = attachment.ContentId,
            });
        }

        return mapped;
    }

    private static void ValidateAddress(string address, EmailDefaultsOptions defaults)
    {
        if (!MailAddress.TryCreate(address, out var parsed))
        {
            throw new ValidationException($"'{address}' is not a valid email address.");
        }

        if (defaults.AllowedRecipientDomains.Count == 0)
        {
            return;
        }

        var allowed = defaults.AllowedRecipientDomains
            .Any(domain => parsed.Host.Equals(domain, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            throw new ValidationException($"Recipient domain '{parsed.Host}' is not allowed.");
        }
    }
}
