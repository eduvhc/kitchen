using MessagingKit.Inbox.Abstractions;
using MessagingKit.Inbox.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessagingKit.Inbox;

public class InboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<InboxOptions> options,
    ILogger<InboxProcessor> logger) : BackgroundService
{
    private readonly InboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Inbox processor disabled by configuration");
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Inbox processor started (batch {BatchSize}, concurrency {Concurrency})",
                _options.BatchSize,
                _options.Concurrency);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunOnceAsync(stoppingToken) == _options.BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbox processing loop failed; backing off");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        var batch = await store.ClaimBatchAsync(
            _options.BatchSize,
            TimeSpan.FromSeconds(_options.LockDurationSeconds),
            ct);

        if (batch.Count == 0)
        {
            return 0;
        }

        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency, CancellationToken = ct },
            async (message, token) => await HandleAsync(message, token));

        return batch.Count;
    }

    public TimeSpan BackoffFor(int attemptCount)
    {
        var exponent = Math.Max(0, attemptCount - 1);
        var seconds = _options.BaseRetryDelaySeconds * Math.Pow(2, Math.Min(exponent, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxRetryDelaySeconds));
    }

    private async Task HandleAsync(InboxMessage message, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var registry = scope.ServiceProvider.GetRequiredService<MessageTypeRegistry>();
        var serializer = scope.ServiceProvider.GetRequiredService<IMessageSerializer>();

        var messageType = registry.Resolve(message.Type);

        if (messageType is null)
        {
            await store.MarkFailedAsync(message, $"No message type registered for '{message.Type}'.", true, TimeSpan.Zero, ct);
            return;
        }

        var handlerType = typeof(IMessageHandler<>).MakeGenericType(messageType);
        var handler = scope.ServiceProvider.GetService(handlerType);

        if (handler is null)
        {
            await store.MarkFailedAsync(message, $"No handler registered for '{message.Type}'.", true, TimeSpan.Zero, ct);
            return;
        }

        var context = new MessageContext
        {
            MessageId = message.Id,
            Type = message.Type,
            AttemptCount = message.AttemptCount,
            Headers = message.Headers,
        };

        try
        {
            var payload = serializer.Deserialize(message.Payload, messageType);
            var method = handlerType.GetMethod(nameof(IMessageHandler<object>.HandleAsync))!;
            await (Task)method.Invoke(handler, [payload, context, ct])!;

            await store.MarkProcessedAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            logger.LogError(inner, "Handler failed for inbox message {MessageId}", message.Id);

            await store.MarkFailedAsync(
                message,
                $"{inner.GetType().Name}: {inner.Message}",
                inner is MessageSerializationException,
                BackoffFor(message.AttemptCount),
                CancellationToken.None);
        }
    }
}
