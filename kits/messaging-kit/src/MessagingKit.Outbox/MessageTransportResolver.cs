using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.Outbox;

internal sealed class MessageTransportResolver : IMessageTransportResolver
{
    private readonly IServiceProvider _provider;
    private readonly Dictionary<string, Type> _byKey;
    private readonly Type? _default;

    public MessageTransportResolver(IServiceProvider provider, IEnumerable<TransportRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _provider = provider;
        _byKey = [];

        foreach (var registration in registrations)
        {
            if (registration.Key is null)
            {
                _default = registration.TransportType;
            }
            else
            {
                _byKey[registration.Key] = registration.TransportType;
            }
        }
    }

    public IMessageTransport Resolve(MessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var transportType = ByDestination(envelope) ?? ByMessageType(envelope) ?? _default;

        if (transportType is not null)
        {
            return (IMessageTransport)_provider.GetRequiredService(transportType);
        }

        // No routing rule: fall back to an IMessageTransport registered directly in DI, which is how
        // hosts wired transports before routing existed.
        return _provider.GetService<IMessageTransport>()
            ?? throw new InvalidOperationException(
                $"No transport is registered for message type '{envelope.Type}', and no default transport was configured. "
                + $"Call UseTransport<T>() for a default, or UseTransport<T>(\"{envelope.Type}\") to route this type.");
    }

    // Destination wins: addressing a specific module is more specific than the message's own type.
    private Type? ByDestination(MessageEnvelope envelope) =>
        envelope.Destination is { Length: > 0 } destination ? _byKey.GetValueOrDefault(destination) : null;

    private Type? ByMessageType(MessageEnvelope envelope) => _byKey.GetValueOrDefault(envelope.Type);
}
