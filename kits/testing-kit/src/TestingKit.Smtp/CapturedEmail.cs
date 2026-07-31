using System.Text.Json.Serialization;

namespace TestingKit.Smtp;

public sealed record CapturedEmail
{
    [JsonPropertyName("ID")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("MessageID")]
    public string MessageId { get; init; } = string.Empty;

    [JsonPropertyName("Subject")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("From")]
    public EmailAddress? From { get; init; }

    [JsonPropertyName("To")]
    public IReadOnlyList<EmailAddress> To { get; init; } = [];

    [JsonPropertyName("Cc")]
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    [JsonPropertyName("Bcc")]
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];

    [JsonPropertyName("Snippet")]
    public string Snippet { get; init; } = string.Empty;

    [JsonPropertyName("Attachments")]
    public int Attachments { get; init; }

    public IReadOnlyList<string> Recipients => [.. To.Select(address => address.Address)];
}

public sealed record EmailAddress
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("Address")]
    public string Address { get; init; } = string.Empty;
}

public sealed record CapturedEmailBody
{
    [JsonPropertyName("HTML")]
    public string Html { get; init; } = string.Empty;

    [JsonPropertyName("Text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("Subject")]
    public string Subject { get; init; } = string.Empty;
}
