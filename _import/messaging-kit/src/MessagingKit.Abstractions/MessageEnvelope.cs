namespace MessagingKit;

public sealed record MessageEnvelope
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public string? Destination { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public required DateTimeOffset CreatedAt { get; init; }

    public int AttemptCount { get; init; }
}
