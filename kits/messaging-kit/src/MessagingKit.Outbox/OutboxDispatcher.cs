using System.Diagnostics;
using MessagingKit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessagingKit.Outbox;

/// <summary>Claims due outbox rows and hands each to its transport, retrying and dead-lettering failures.</summary>
/// <remarks>
/// Resolve the one <c>AddOutbox</c> registers: it reports metrics on the container's meter factory. An
/// instance built with this public constructor still emits spans but reports no metrics.
/// </remarks>
public class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    OutboxSignal signal,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;
    private readonly OutboxMetrics? _metrics;
    private readonly TimeProvider _clock = TimeProvider.System;
    private long? _nextBacklogSample;

    internal OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        OutboxSignal signal,
        ILogger<OutboxDispatcher> logger,
        OutboxMetrics metrics,
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

    /// <summary>Claims one batch of due rows and delivers them.</summary>
    /// <returns>How many rows were claimed; <c>0</c> when nothing was due.</returns>
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

    /// <summary>
    /// Counts pending and dead rows into the backlog instrument, at most once per
    /// <see cref="OutboxOptions.BacklogSampleIntervalSeconds"/> and only while something listens to it.
    /// A failed count is logged and the last one kept, so the loop carries on delivering.
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
            var (pending, dead) = await scope.ServiceProvider.GetRequiredService<IOutboxBacklog>().CountAsync(ct);
            _metrics.UpdateBacklog(pending, dead);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Counting the outbox backlog failed; the backlog instrument keeps its last count");
        }
    }

    /// <summary>The delay before retrying a row that has failed <paramref name="attemptCount"/> times.</summary>
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

        // Not the creation context (that is the caller's, carried in the headers), so a client span per
        // the messaging conventions. Named by type: a destination is whatever the caller passed.
        using var activity = MessagingDiagnostics.StartActivity(
            $"send {message.Type}",
            ActivityKind.Client,
            message.Headers);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(MessagingTags.System, MessagingTags.SystemValue);
            activity.SetTag(MessagingTags.OperationName, "send");
            activity.SetTag(MessagingTags.OperationType, "send");
            activity.SetTag(MessagingTags.MessageId, message.Id.ToString());
            activity.SetTag(MessagingTags.DestinationName, message.Destination);
            activity.SetTag(MessagingTags.MessageType, message.Type);
            activity.SetTag(MessagingTags.Attempt, message.AttemptCount);
        }

        var started = Stopwatch.GetTimestamp();
        string outcome;
        string? errorType = null;
        string? failure = null;

        // Recorded only after the try, so a misbehaving metrics listener can never send a delivered
        // message down the failure path.
        try
        {
            var transport = resolver.Resolve(envelope);
            var result = await transport.SendAsync(envelope, ct);

            if (result.IsSuccess)
            {
                await store.MarkSentAsync(message, ct);
                outcome = OutboxMetrics.Sent;
            }
            else
            {
                var permanent = !result.Error.IsTransient;

                await store.MarkFailedAsync(
                    message,
                    result.Error.ToString(),
                    permanent,
                    BackoffFor(message.AttemptCount),
                    ct);

                outcome = FailureOutcome(message, permanent);
                errorType = result.Error.Code;
                failure = result.Error.Message;
            }
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
            activity?.AddException(ex);
            outcome = FailureOutcome(message, true);
            errorType = ex.GetType().FullName ?? MessagingTags.OtherError;
            failure = ex.Message;
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

            activity?.AddException(ex);
            outcome = FailureOutcome(message, false);
            errorType = ex.GetType().FullName ?? MessagingTags.OtherError;
            failure = ex.Message;
        }

        activity?.SetTag(MessagingTags.Outcome, outcome);

        if (errorType is not null)
        {
            activity?.SetTag(MessagingTags.ErrorType, errorType);
            activity?.SetStatus(ActivityStatusCode.Error, failure);
        }

        _metrics?.RecordDelivery(message.Type, outcome, errorType, started);
    }

    private static string FailureOutcome(OutboxMessage message, bool permanent) =>
        message.IsExhaustedAfterFailure(permanent) ? OutboxMetrics.Dead : OutboxMetrics.Retried;
}
