using System.Diagnostics;
using System.Diagnostics.Metrics;
using MessagingKit.Abstractions;

namespace MessagingKit.Outbox;

/// <summary>The outbox's instruments, on a meter from the container's <see cref="IMeterFactory"/>.</summary>
internal sealed class OutboxMetrics
{
    public const string Sent = "sent";
    public const string Retried = "retried";
    public const string Dead = MessagingTags.Dead;

    // The messaging semconv boundaries. The SDK defaults are sized for milliseconds, not seconds.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private readonly Counter<long> _sentMessages;
    private readonly Histogram<double> _operationDuration;
    private readonly ObservableUpDownCounter<long> _backlog;

    // Written by the dispatcher loop, read by the collection callback on another thread.
    private Backlog? _lastBacklog;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MessagingKitInstrumentation.MeterName);

        _sentMessages = meter.CreateCounter<long>(
            "messaging.client.sent.messages",
            "{message}",
            "Outbox delivery attempts handed to a transport, by outcome.");

        _operationDuration = meter.CreateHistogram(
            "messaging.client.operation.duration",
            "s",
            "Duration of one outbox delivery attempt, from claim to the row being marked.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

        _backlog = meter.CreateObservableUpDownCounter(
            "messagingkit.outbox.messages",
            ObserveBacklog,
            "{message}",
            "Outbox rows not yet sent, by status, as last sampled by the dispatcher.");
    }

    /// <summary>Whether anything listens to the backlog; the dispatcher skips counting rows when nothing does.</summary>
    public bool BacklogObserved => _backlog.Enabled;

    public void RecordDelivery(string messageType, string outcome, string? errorType, long startTimestamp)
    {
        if (!_sentMessages.Enabled && !_operationDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { MessagingTags.System, MessagingTags.SystemValue },
            { MessagingTags.OperationName, "send" },
            { MessagingTags.OperationType, "send" },
            { MessagingTags.MessageType, messageType },
            { MessagingTags.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(MessagingTags.ErrorType, errorType);
        }

        _sentMessages.Add(1, tags);
        _operationDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    public void UpdateBacklog(long pending, long dead) => Volatile.Write(ref _lastBacklog, new Backlog(pending, dead));

    // Reads the cached sample only. Collection callbacks run in sequence, so one that queried the
    // database would stall every other instrument in the process.
    private IEnumerable<Measurement<long>> ObserveBacklog()
    {
        var backlog = Volatile.Read(ref _lastBacklog);

        if (backlog is null)
        {
            return [];
        }

        return
        [
            new Measurement<long>(backlog.Pending, StatusTags(MessagingTags.Pending)),
            new Measurement<long>(backlog.Dead, StatusTags(MessagingTags.Dead)),
        ];
    }

    private static KeyValuePair<string, object?>[] StatusTags(string status) =>
    [
        new(MessagingTags.System, MessagingTags.SystemValue),
        new(MessagingTags.MessageStatus, status),
    ];

    private sealed record Backlog(long Pending, long Dead);
}
