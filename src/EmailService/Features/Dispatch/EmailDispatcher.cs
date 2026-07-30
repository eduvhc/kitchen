using EmailService.Features.Emails.Abstractions;
using EmailService.Features.Emails.Domain;
using EmailService.Options;
using EmailService.Transport.Abstractions;
using Microsoft.Extensions.Options;

namespace EmailService.Features.Dispatch;

public class EmailDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<DispatcherOptions> options,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    private readonly DispatcherOptions _options = options.Value;

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

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();

        var batch = await queue.ClaimBatchAsync(
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
        var queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        try
        {
            var result = await sender.SendAsync(message, ct);

            if (result.Success)
            {
                await queue.MarkSentAsync(message, result.ProviderMessageId, ct);
                return;
            }

            await queue.MarkFailedAsync(
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

            await queue.MarkFailedAsync(
                message,
                $"{ex.GetType().Name}: {ex.Message}",
                false,
                BackoffFor(message.AttemptCount),
                CancellationToken.None);
        }
    }
}
