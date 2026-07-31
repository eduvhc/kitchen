using System.Diagnostics;
using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessagingKit.Outbox;

public class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    OutboxSignal signal,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Outbox dispatcher disabled by configuration");
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Outbox dispatcher started (batch {BatchSize}, concurrency {Concurrency})",
                _options.BatchSize,
                _options.Concurrency);
        }

        var pollInterval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

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
                logger.LogError(ex, "Outbox dispatch loop failed; backing off");
            }

            try
            {
                // Wakes on a commit that staged rows; the interval is only the fallback.
                await signal.WaitAsync(pollInterval, stoppingToken);
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
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

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
            async (message, token) => await DeliverAsync(message, token));

        return batch.Count;
    }

    public TimeSpan BackoffFor(int attemptCount)
    {
        var exponent = Math.Max(0, attemptCount - 1);
        var seconds = _options.BaseRetryDelaySeconds * Math.Pow(2, Math.Min(exponent, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxRetryDelaySeconds));
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var resolver = scope.ServiceProvider.GetRequiredService<IMessageTransportResolver>();

        var envelope = new MessageEnvelope
        {
            Id = message.Id,
            Type = message.Type,
            Payload = message.Payload,
            Destination = message.Destination,
            Headers = message.Headers,
            CreatedAt = message.CreatedAt,
            AttemptCount = message.AttemptCount,
        };

        using var activity = MessagingDiagnostics.StartActivity(
            $"send {message.Type}",
            ActivityKind.Producer,
            message.Headers);

        activity?.SetTag("messaging.system", "messagingkit");
        activity?.SetTag("messaging.operation", "send");
        activity?.SetTag("messaging.message.id", message.Id);
        activity?.SetTag("messaging.message.type", message.Type);
        activity?.SetTag("messaging.attempt", message.AttemptCount);

        try
        {
            var transport = resolver.Resolve(envelope);
            var result = await transport.SendAsync(envelope, ct);

            if (result.Success)
            {
                await store.MarkSentAsync(message, ct);
                return;
            }

            activity?.SetStatus(ActivityStatusCode.Error, result.Error);

            await store.MarkFailedAsync(
                message,
                result.Error ?? "Unknown transport failure",
                result.IsPermanent,
                BackoffFor(message.AttemptCount),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // A missing transport registration is a wiring bug; retrying it forever hides that.
            logger.LogError(ex, "No transport for {MessageId} of type {MessageType}", message.Id, message.Type);

            await store.MarkFailedAsync(message, ex.Message, true, TimeSpan.Zero, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled failure dispatching {MessageId}", message.Id);

            await store.MarkFailedAsync(
                message,
                $"{ex.GetType().Name}: {ex.Message}",
                false,
                BackoffFor(message.AttemptCount),
                CancellationToken.None);
        }
    }
}
