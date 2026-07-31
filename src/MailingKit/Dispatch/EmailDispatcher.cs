using MailingKit.Emails.Abstractions;
using MailingKit.Emails.Domain;
using MailingKit.Options;
using MailingKit.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailingKit.Dispatch;

/// <summary>
/// Polls the emails table, claims a batch with <c>FOR UPDATE SKIP LOCKED</c>, and sends. Safe to run
/// in every host replica; set <c>Dispatcher.Enabled = false</c> on replicas that should not send.
/// </summary>
public sealed class EmailDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<MailingKitOptions> options,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    private readonly DispatcherOptions _options = options.Value.Dispatcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Email dispatcher disabled by configuration");
            return;
        }

        logger.LogInformation(
            "Email dispatcher started (batch {BatchSize}, concurrency {Concurrency})",
            _options.BatchSize,
            _options.Concurrency);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await RunOnceAsync(stoppingToken);
                if (processed == _options.BatchSize)
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
                logger.LogError(ex, "Dispatcher loop failed; backing off");
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

        logger.LogInformation("Email dispatcher stopped");
    }

    /// <summary>Claims and processes one batch. Public so hosts and tests can drive it directly.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IEmailDispatchStore>();

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
            async (message, token) => await ProcessAsync(message, token));

        return batch.Count;
    }

    public TimeSpan BackoffFor(int attemptCount)
    {
        var exponent = Math.Max(0, attemptCount - 1);
        var seconds = _options.BaseRetryDelaySeconds * Math.Pow(2, Math.Min(exponent, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxRetryDelaySeconds));
    }

    private async Task ProcessAsync(EmailMessage message, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IEmailDispatchStore>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        try
        {
            var result = await sender.SendAsync(message, ct);

            if (result.Success)
            {
                await store.MarkSentAsync(message, result.ProviderMessageId, ct);
                return;
            }

            await store.MarkFailedAsync(
                message,
                result.Error ?? "Unknown send failure",
                result.IsPermanent,
                BackoffFor(message.AttemptCount),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled failure sending {EmailId}", message.Id);

            await store.MarkFailedAsync(
                message,
                $"{ex.GetType().Name}: {ex.Message}",
                false,
                BackoffFor(message.AttemptCount),
                CancellationToken.None);
        }
    }
}
