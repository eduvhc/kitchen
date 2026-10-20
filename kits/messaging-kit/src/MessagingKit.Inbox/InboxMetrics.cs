using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MessagingKit.Inbox;

internal static class InboxMetrics
{
    public const string Processed = "processed";
    public const string Retried = "retried";
    public const string Dead = "dead";

    public const string Stored = "stored";
    public const string Duplicate = "duplicate";

    // The messaging semconv boundaries. The SDK defaults are sized for milliseconds, not seconds.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private static readonly Counter<long> ConsumedMessages = MessagingDiagnostics.Meter.CreateCounter<long>(
        "messaging.client.consumed.messages",
        "{message}",
        "Inbox messages handed to a handler, by outcome.");

    private static readonly Histogram<double> ProcessDuration = MessagingDiagnostics.Meter.CreateHistogram(
        "messaging.process.duration",
        "s",
        "Duration of one inbox handling attempt, from claim to the row being marked.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

    private static readonly Counter<long> ReceivedMessages = MessagingDiagnostics.Meter.CreateCounter<long>(
        "messagingkit.inbox.received.messages",
        "{message}",
        "Messages offered to the inbox, by whether they were stored or dropped as duplicates.");

    public static void RecordHandling(string messageType, string outcome, string? errorType, long startTimestamp)
    {
        if (!ConsumedMessages.Enabled && !ProcessDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { "messaging.system", "messagingkit" },
            { "messaging.operation.name", "process" },
            { "messaging.operation.type", "process" },
            { "messaging.message.type", messageType },
            { "messagingkit.outcome", outcome },
        };

        if (errorType is not null)
        {
            tags.Add("error.type", errorType);
        }

        ConsumedMessages.Add(1, tags);
        ProcessDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    public static void RecordReceived(string messageType, string outcome)
    {
        if (!ReceivedMessages.Enabled)
        {
            return;
        }

        ReceivedMessages.Add(
            1,
            new KeyValuePair<string, object?>("messaging.system", "messagingkit"),
            new KeyValuePair<string, object?>("messaging.message.type", messageType),
            new KeyValuePair<string, object?>("messagingkit.outcome", outcome));
    }
}
