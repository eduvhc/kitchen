using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MessagingKit.Outbox;

internal static class OutboxMetrics
{
    public const string Sent = "sent";
    public const string Retried = "retried";
    public const string Dead = "dead";

    // The messaging semconv boundaries. The SDK defaults are sized for milliseconds, not seconds.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private static readonly Counter<long> SentMessages = MessagingDiagnostics.Meter.CreateCounter<long>(
        "messaging.client.sent.messages",
        "{message}",
        "Outbox delivery attempts handed to a transport, by outcome.");

    private static readonly Histogram<double> OperationDuration = MessagingDiagnostics.Meter.CreateHistogram(
        "messaging.client.operation.duration",
        "s",
        "Duration of one outbox delivery attempt, from claim to the row being marked.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

    public static void RecordDelivery(string messageType, string outcome, string? errorType, long startTimestamp)
    {
        if (!SentMessages.Enabled && !OperationDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { "messaging.system", "messagingkit" },
            { "messaging.operation.name", "send" },
            { "messaging.operation.type", "send" },
            { "messaging.message.type", messageType },
            { "messagingkit.outcome", outcome },
        };

        if (errorType is not null)
        {
            tags.Add("error.type", errorType);
        }

        SentMessages.Add(1, tags);
        OperationDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }
}
