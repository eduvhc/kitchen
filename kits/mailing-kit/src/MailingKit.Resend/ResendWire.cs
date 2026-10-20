using System.Text.Json.Serialization;

namespace MailingKit.Resend;

/// <summary>The body of <c>POST /emails</c>.</summary>
internal sealed record ResendEmailRequest
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required IReadOnlyList<string> To { get; init; }

    [JsonPropertyName("cc")]
    public IReadOnlyList<string>? Cc { get; init; }

    [JsonPropertyName("bcc")]
    public IReadOnlyList<string>? Bcc { get; init; }

    [JsonPropertyName("reply_to")]
    public string? ReplyTo { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("html")]
    public string? Html { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    [JsonPropertyName("attachments")]
    public IReadOnlyList<ResendAttachment>? Attachments { get; init; }
}

internal sealed record ResendAttachment
{
    /// <summary>Base64, passed through as MailingKit already holds it.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("filename")]
    public required string FileName { get; init; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    [JsonPropertyName("content_id")]
    public string? ContentId { get; init; }
}

/// <summary>A 2xx response: <c>{ "id": "…" }</c>.</summary>
internal sealed record ResendEmailResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>An error response: <c>{ "statusCode": 422, "name": "validation_error", "message": "…" }</c>.</summary>
internal sealed record ResendErrorResponse
{
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResendEmailRequest))]
[JsonSerializable(typeof(ResendEmailResponse))]
[JsonSerializable(typeof(ResendErrorResponse))]
internal sealed partial class ResendJsonContext : JsonSerializerContext;
