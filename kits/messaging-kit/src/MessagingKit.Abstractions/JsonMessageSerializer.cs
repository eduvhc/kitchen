using System.Text.Json;

namespace MessagingKit;

public sealed class JsonMessageSerializer(JsonSerializerOptions? options = null) : IMessageSerializer
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public string Serialize(object message) => JsonSerializer.Serialize(message, _options);

    public object Deserialize(string payload, Type type) =>
        JsonSerializer.Deserialize(payload, type, _options)
        ?? throw new MessageSerializationException($"Payload deserialized to null for type '{type.Name}'.");
}

public sealed class MessageSerializationException(string message) : Exception(message);
