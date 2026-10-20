namespace TestingKit.RabbitMq;

public sealed class RabbitMqContainerOptions : ContainerOptions
{
    public const string DefaultImage = "rabbitmq:4.1-management";

    public RabbitMqContainerOptions() => Image = DefaultImage;
}

public sealed class RabbitMqClientOptions : ClientOptions
{
    public bool ReuseConnection { get; set; }

    public IList<string> QueuesToPurge { get; } = [];
}
