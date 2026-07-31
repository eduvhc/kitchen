namespace MessagingKit;

/// <summary>
/// Picks the transport for an envelope. Lets one host route some message types in-process and
/// others over a broker.
/// </summary>
public interface IMessageTransportResolver
{
    /// <summary>
    /// Returns the transport registered for <paramref name="envelope"/>'s type, falling back to the
    /// default transport.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No transport matches the type and no default is registered.
    /// </exception>
    IMessageTransport Resolve(MessageEnvelope envelope);
}
