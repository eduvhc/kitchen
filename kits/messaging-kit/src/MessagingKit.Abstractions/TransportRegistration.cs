namespace MessagingKit;

/// <summary>
/// One transport routing rule. <see cref="Key"/> matches either a message's
/// <see cref="MessageEnvelope.Destination"/> or its <see cref="MessageEnvelope.Type"/>, with
/// destination taking precedence — addressing a specific module is more specific than routing every
/// message of a type the same way. A null key registers the default transport.
/// </summary>
public sealed class TransportRegistration(string? key, Type transportType)
{
    public string? Key { get; } = key;

    public Type TransportType { get; } = transportType ?? throw new ArgumentNullException(nameof(transportType));
}
