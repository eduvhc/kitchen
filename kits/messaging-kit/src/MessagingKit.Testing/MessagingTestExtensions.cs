using MessagingKit.Inbox;
using MessagingKit.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.Testing;

public static class MessagingTestExtensions
{
    /// <summary>
    /// Runs the outbox and inbox to completion, so a test can assert on the effect of a message
    /// instead of sleeping until the background loops happen to run.
    /// </summary>
    /// <remarks>
    /// Both halves are drained repeatedly, because handling one message can produce another. Give
    /// this a real host — it drives the same dispatcher and processor production uses, so what a test
    /// exercises is what ships.
    /// </remarks>
    /// <param name="maxRounds">
    /// Guards against a handler that enqueues a message which causes another, forever. Exceeding it
    /// throws rather than hanging the test run.
    /// </param>
    public static async Task<int> DrainMessagingAsync(
        this IServiceProvider services,
        int maxRounds = 50,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRounds, 1);

        var dispatcher = services.GetRequiredService<OutboxDispatcher>();
        var processor = services.GetRequiredService<InboxProcessor>();

        var handled = 0;

        for (var round = 0; round < maxRounds; round++)
        {
            var dispatched = await dispatcher.RunOnceAsync(ct);
            var processed = await processor.RunOnceAsync(ct);

            handled += dispatched + processed;

            if (dispatched == 0 && processed == 0)
            {
                return handled;
            }
        }

        throw new InvalidOperationException(
            $"Messaging did not settle within {maxRounds} rounds. A handler is most likely producing "
            + "a message that causes another one.");
    }

    /// <summary>
    /// Drains only the outbox, leaving delivered messages sitting in the inbox. Useful when a test
    /// wants to assert on what was delivered before anything handles it.
    /// </summary>
    public static Task<int> DrainOutboxAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<OutboxDispatcher>().RunOnceAsync(ct);
    }

    /// <summary>Drains only the inbox.</summary>
    public static Task<int> DrainInboxAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<InboxProcessor>().RunOnceAsync(ct);
    }
}
