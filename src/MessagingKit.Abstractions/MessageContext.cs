namespace MessagingKit;

public sealed record MessageContext
{
    public required Guid MessageId { get; init; }

    public required string Type { get; init; }

    public required int AttemptCount { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}
