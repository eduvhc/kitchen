namespace MessagingKit;

public sealed class MessageTypeRegistry
{
    private readonly Dictionary<string, Type> _byName = [];
    private readonly Dictionary<Type, string> _byType = [];

    public MessageTypeRegistry()
    {
    }

    public MessageTypeRegistry(IEnumerable<IMessageTypeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (var registration in registrations)
        {
            Register(registration.Name, registration.MessageType);
        }
    }

    public void Register(string name, Type type)
    {
        _byName[name] = type;
        _byType[type] = name;
    }

    public string NameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Falls back to the convention rather than FullName: a namespace move should not rename the
        // message and orphan rows already on the wire.
        return _byType.TryGetValue(type, out var name) ? name : MessageName.For(type);
    }

    public Type? Resolve(string name) => _byName.GetValueOrDefault(name);
}
