namespace MessagingKit;

/// <summary>
/// Pins the wire name of a message type. Without it the name is derived from the type name
/// (<c>SendEmail</c> becomes <c>send-email</c>), which means renaming the class renames the message
/// and in-flight rows stop resolving. Apply this to anything already in production.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class MessageAttribute(string name) : Attribute
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Message name cannot be empty.", nameof(name))
        : name;
}
