using System.ComponentModel.DataAnnotations;

namespace EmailService.Features.Emails.SendEmail;

public record SendEmailRequest
{
    public List<string> To { get; init; } = [];
    public List<string> Cc { get; init; } = [];
    public List<string> Bcc { get; init; } = [];

    public string? From { get; init; }
    public string? FromName { get; init; }
    public string? ReplyTo { get; init; }

    public string? Subject { get; init; }
    public string? Html { get; init; }
    public string? Text { get; init; }

    public string? Template { get; init; }
    public Dictionary<string, object?> Model { get; init; } = [];

    public List<AttachmentDto> Attachments { get; init; } = [];
    public Dictionary<string, string> Headers { get; init; } = [];

    public string? IdempotencyKey { get; init; }
    public DateTimeOffset? SendAt { get; init; }

    [Range(1, 20)]
    public int? MaxAttempts { get; init; }
}
