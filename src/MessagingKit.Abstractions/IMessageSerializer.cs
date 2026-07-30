namespace MessagingKit;

public interface IMessageSerializer
{
    string Serialize(object message);

    object Deserialize(string payload, Type type);
}
