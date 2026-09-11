using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
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

/// <summary>A consumed message with the headers a body-only read throws away.</summary>
/// <param name="Body">The UTF-8 message body.</param>
/// <param name="Headers">
/// Headers as they came off the wire. AMQP field tables carry strings as <see langword="byte"/>[],
/// so read text values through <see cref="GetHeaderString"/> rather than casting.
/// </param>
public sealed record RabbitMqMessage(string Body, IReadOnlyDictionary<string, object?> Headers)
{
    /// <summary>
    /// Reads a header as text, decoding the <see langword="byte"/>[] an AMQP field table actually
    /// holds. Returns <see langword="null"/> when the header is absent.
    /// </summary>
    public string? GetHeaderString(string name) =>
        Headers.TryGetValue(name, out var value)
            ? value switch
            {
                null => null,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => value.ToString(),
            }
            : null;
}

public class RabbitMqFixture(
    RabbitMqContainerOptions? containerOptions = null,
    RabbitMqClientOptions? clientOptions = null)
    : TestFixtureBase<RabbitMqContainerOptions, RabbitMqClientOptions>(containerOptions, clientOptions), IResettableFixture
{
    private const ushort NotFound = 404;

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
        => await PublishAsync(exchange, queue, message, headers: null, exchangeType, ct);

    /// <summary>Publishes with extra headers — for asserting what a consumer reads back off the wire.</summary>
    public async Task PublishAsync<T>(
        string exchange,
        string queue,
        T message,
        IReadOnlyDictionary<string, object?>? headers,
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

            var messageHeaders = new Dictionary<string, object?> { ["exchange"] = exchange };

            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    messageHeaders[key] = value;
                }
            }

            await channel.BasicPublishAsync(
                exchange,
                string.Empty,
                false,
                new BasicProperties { Headers = messageHeaders },
                body,
                ct);
        }

        await ReleaseAsync(connection);
    }

    /// <summary>
    /// Consumes a message with its headers. Use this instead of
    /// <see cref="ConsumeAsync(string, TimeSpan?, CancellationToken)"/> when the consumer under test
    /// reads message metadata — trace context, correlation ids, routing hints.
    /// </summary>
    public async Task<RabbitMqMessage?> ConsumeMessageAsync(
        string queue,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
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
                    var headers = result.BasicProperties.Headers is { } wireHeaders
                        ? new Dictionary<string, object?>(wireHeaders)
                        : [];

                    return new RabbitMqMessage(Encoding.UTF8.GetString(result.Body.ToArray()), headers);
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

    /// <summary>
    /// Empties a queue. A queue that does not exist yet is already empty, so that case is a no-op
    /// rather than an error — otherwise a reset before the first publish fails the run.
    /// </summary>
    public async Task PurgeAsync(string queue, CancellationToken ct = default)
    {
        EnsureReady();
        var connection = await GetConnectionAsync();

        try
        {
            // The broker closes the channel on a 404, so this gets one of its own.
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
            await channel.QueuePurgeAsync(queue, ct);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == NotFound)
        {
            // Nothing to empty.
        }
        finally
        {
            await ReleaseAsync(connection);
        }
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
