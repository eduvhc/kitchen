using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

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

public class RabbitMqFixture(
    RabbitMqContainerOptions? containerOptions = null,
    RabbitMqClientOptions? clientOptions = null)
    : TestFixtureBase<RabbitMqContainerOptions, RabbitMqClientOptions>(containerOptions, clientOptions), IResettableFixture
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    private RabbitMqContainer? _container;
    private IConnection? _cachedConnection;

    public async Task PublishAsync<T>(
        string exchange,
        string queue,
        T message,
        string exchangeType = "fanout",
        CancellationToken ct = default)
        where T : class
    {
        EnsureReady();
        var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        var connection = await GetConnectionAsync();

        await using (var channel = await connection.CreateChannelAsync(cancellationToken: ct))
        {
            await channel.ExchangeDeclareAsync(exchange, exchangeType, durable: true, cancellationToken: ct);
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await channel.QueueBindAsync(queue, exchange, string.Empty, cancellationToken: ct);

            var properties = new BasicProperties
            {
                Headers = new Dictionary<string, object?> { ["exchange"] = exchange },
            };

            await channel.BasicPublishAsync(exchange, string.Empty, false, properties, body, ct);
        }

        await ReleaseAsync(connection);
    }

    public async Task<string?> ConsumeAsync(string queue, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        EnsureReady();
        var connection = await GetConnectionAsync();
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        try
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

            while (DateTimeOffset.UtcNow < deadline)
            {
                var result = await channel.BasicGetAsync(queue, autoAck: true, ct);

                if (result is not null)
                {
                    return Encoding.UTF8.GetString(result.Body.ToArray());
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            return null;
        }
        finally
        {
            await ReleaseAsync(connection);
        }
    }

    public async Task<T?> ConsumeAsync<T>(string queue, TimeSpan? timeout = null, CancellationToken ct = default)
        where T : class
    {
        var json = await ConsumeAsync(queue, timeout, ct);
        return json is null ? null : JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    public async Task PurgeAsync(string queue, CancellationToken ct = default)
    {
        EnsureReady();
        var connection = await GetConnectionAsync();

        await using (var channel = await connection.CreateChannelAsync(cancellationToken: ct))
        {
            await channel.QueuePurgeAsync(queue, ct);
        }

        await ReleaseAsync(connection);
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        foreach (var queue in Client.QueuesToPurge)
        {
            await PurgeAsync(queue, ct);
        }
    }

    protected override async Task StartContainerAsync(CancellationToken ct)
    {
        var builder = new RabbitMqBuilder(Container.Image!)
            .WithReuse(Container.Reuse);

        foreach (var (key, value) in Container.Labels)
        {
            builder = builder.WithLabel(key, value);
        }

        _container = builder.Build();
        await _container.StartAsync(ct);
        ConnectionString = _container.GetConnectionString();
    }

    protected override async ValueTask DisposeContainerAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override async Task OnBeforeDisposeAsync()
    {
        if (_cachedConnection is not null)
        {
            await _cachedConnection.DisposeAsync();
            _cachedConnection = null;
        }
    }

    private async Task<IConnection> GetConnectionAsync()
    {
        if (!Client.ReuseConnection)
        {
            return await CreateConnectionAsync();
        }

        if (_cachedConnection is null || !_cachedConnection.IsOpen)
        {
            if (_cachedConnection is not null)
            {
                await _cachedConnection.DisposeAsync();
            }

            _cachedConnection = await CreateConnectionAsync();
        }

        return _cachedConnection;
    }

    private async Task<IConnection> CreateConnectionAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
        return await factory.CreateConnectionAsync();
    }

    private async Task ReleaseAsync(IConnection connection)
    {
        if (!Client.ReuseConnection)
        {
            await connection.DisposeAsync();
        }
    }
}
