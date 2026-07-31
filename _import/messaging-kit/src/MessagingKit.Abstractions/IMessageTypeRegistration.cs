namespace MessagingKit;

public interface IMessageTypeRegistration
{
    string Name { get; }

    Type MessageType { get; }
}

public sealed record MessageTypeRegistration(string Name, Type MessageType) : IMessageTypeRegistration;
