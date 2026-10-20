using System.Diagnostics;
using MessagingKit.Abstractions;
using MessagingKit.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessagingKit.Inbox;

/// <summary>Claims due inbox rows and runs each through its handler, retrying and dead-lettering failures.</summary>
/// <remarks>
/// Resolve the one <c>AddInbox</c> registers: it reports metrics on the container's meter factory. An
/// instance built with this public constructor still emits spans but reports no metrics.
/// </remarks>
public class InboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<InboxOptions> options,
    InboxSignal signal,
    ILogger<InboxProcessor> logger) : BackgroundService
{
    private readonly InboxOptions _options = options.Value;
    private readonly InboxMetrics? _metrics;
    private readonly TimeProvider _clock = TimeProvider.System;
    private long? _nextBacklogSample;

    internal InboxProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<InboxOptions> options,
        InboxSignal signal,
        ILogger<InboxProcessor> logger,
        InboxMetrics metrics,
        TimeProvider clock)
        : this(scopeFactory, options, signal, logger)
    {
        _metrics = metrics;
        _clock = clock;
    }

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

        var pollInterval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleBacklogIfDueAsync(stoppingToken);

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
                // Wakes as soon as a message is stored; the interval is only the fallback.
                await signal.WaitAsync(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Claims one batch of due rows and handles them.</summary>
    /// <returns>How many rows were claimed; <c>0</c> when nothing was due.</returns>
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

    /// <summary>
    /// Counts pending and dead rows into the backlog instrument, at most once per
    /// <see cref="InboxOptions.BacklogSampleIntervalSeconds"/> and only while something listens to it.
    /// A failed count is logged and the last one kept, so the loop carries on handling.
    /// </summary>
    internal async Task SampleBacklogIfDueAsync(CancellationToken ct)
    {
        if (_metrics is null || _options.BacklogSampleIntervalSeconds <= 0 || !_metrics.BacklogObserved)
        {
            return;
        }

        var now = _clock.GetTimestamp();

        if (now < _nextBacklogSample)
        {
            return;
        }

        _nextBacklogSample = now + (_options.BacklogSampleIntervalSeconds * _clock.TimestampFrequency);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var (pending, dead) = await scope.ServiceProvider.GetRequiredService<IInboxBacklog>().CountAsync(ct);
            _metrics.UpdateBacklog(pending, dead);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Counting the inbox backlog failed; the backlog instrument keeps its last count");
        }
    }

    /// <summary>The delay before retrying a row that has failed <paramref name="attemptCount"/> times.</summary>
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

        // Parented to the producer's trace, carried in the headers since the message was staged, so
        // the send and this handling read as one trace rather than two unrelated ones.
        using var activity = MessagingDiagnostics.StartActivity(
            $"process {message.Type}",
            ActivityKind.Consumer,
            message.Headers);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(MessagingTags.System, MessagingTags.SystemValue);
            activity.SetTag(MessagingTags.OperationName, "process");
            activity.SetTag(MessagingTags.OperationType, "process");
            activity.SetTag(MessagingTags.MessageId, message.Id.ToString());
            activity.SetTag(MessagingTags.MessageType, message.Type);
            activity.SetTag(MessagingTags.Attempt, message.AttemptCount);
        }

        var started = Stopwatch.GetTimestamp();
        string outcome;
        string? errorType = null;
        string? failure = null;

        var messageType = registry.Resolve(message.Type);
        var handlerType = messageType is null ? null : typeof(IMessageHandler<>).MakeGenericType(messageType);
        var handler = handlerType is null ? null : scope.ServiceProvider.GetService(handlerType);

        // Recorded only after the work, so a misbehaving metrics listener can never send a handled
        // message down the failure path.
        if (messageType is null || handlerType is null || handler is null)
        {
            // A wiring bug, not a transient fault: dead-letter now rather than burying it under retries.
            failure = messageType is null
                ? $"No message type registered for '{message.Type}'."
                : $"No handler registered for '{message.Type}'.";
            errorType = messageType is null ? "unknown_message_type" : "no_handler";

            await store.MarkFailedAsync(message, failure, true, TimeSpan.Zero, ct);
            outcome = FailureOutcome(message, true);
        }
        else
        {
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
                outcome = InboxMetrics.Processed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                logger.LogError(inner, "Handler failed for inbox message {MessageId}", message.Id);

                var permanent = inner is MessageSerializationException;

                await store.MarkFailedAsync(
                    message,
                    $"{inner.GetType().Name}: {inner.Message}",
                    permanent,
                    BackoffFor(message.AttemptCount),
                    CancellationToken.None);

                activity?.AddException(inner);
                outcome = FailureOutcome(message, permanent);
                errorType = inner.GetType().FullName ?? MessagingTags.OtherError;
                failure = inner.Message;
            }
        }

        activity?.SetTag(MessagingTags.Outcome, outcome);

        if (errorType is not null)
        {
            activity?.SetTag(MessagingTags.ErrorType, errorType);
            activity?.SetStatus(ActivityStatusCode.Error, failure);
        }

        _metrics?.RecordHandling(message.Type, outcome, errorType, started);
    }

    private static string FailureOutcome(InboxMessage message, bool permanent) =>
        message.IsExhaustedAfterFailure(permanent) ? InboxMetrics.Dead : InboxMetrics.Retried;
}
