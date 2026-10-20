using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MessagingKit.Inbox;

/// <summary>The inbox's instruments, on a meter from the container's <see cref="IMeterFactory"/>.</summary>
internal sealed class InboxMetrics
{
    public const string Processed = "processed";
    public const string Retried = "retried";
    public const string Dead = MessagingTags.Dead;

    public const string Stored = "stored";
    public const string Duplicate = "duplicate";

    // The messaging semconv boundaries. The SDK defaults are sized for milliseconds, not seconds.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private readonly Counter<long> _consumedMessages;
    private readonly Histogram<double> _processDuration;
    private readonly Counter<long> _receivedMessages;
    private readonly ObservableUpDownCounter<long> _backlog;

    // Written by the processor loop, read by the collection callback on another thread.
    private Backlog? _lastBacklog;

    public InboxMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MessagingKitInstrumentation.MeterName);

        _consumedMessages = meter.CreateCounter<long>(
            "messaging.client.consumed.messages",
            "{message}",
            "Inbox messages handed to a handler, by outcome.");

        _processDuration = meter.CreateHistogram(
            "messaging.process.duration",
            "s",
            "Duration of one inbox handling attempt, from claim to the row being marked.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

        _receivedMessages = meter.CreateCounter<long>(
            "messagingkit.inbox.received.messages",
            "{message}",
            "Messages offered to the inbox, by whether they were stored or dropped as duplicates.");

        _backlog = meter.CreateObservableUpDownCounter(
            "messagingkit.inbox.messages",
            ObserveBacklog,
            "{message}",
            "Inbox rows not yet processed, by status, as last sampled by the processor.");
    }

    /// <summary>Whether anything listens to the backlog; the processor skips counting rows when nothing does.</summary>
    public bool BacklogObserved => _backlog.Enabled;

    public void RecordHandling(string messageType, string outcome, string? errorType, long startTimestamp)
    {
        if (!_consumedMessages.Enabled && !_processDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { MessagingTags.System, MessagingTags.SystemValue },
            { MessagingTags.OperationName, "process" },
            { MessagingTags.OperationType, "process" },
            { MessagingTags.MessageType, messageType },
            { MessagingTags.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(MessagingTags.ErrorType, errorType);
        }

        _consumedMessages.Add(1, tags);
        _processDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    public void RecordReceived(string messageType, string outcome)
    {
        if (!_receivedMessages.Enabled)
        {
            return;
        }

        _receivedMessages.Add(
            1,
            new KeyValuePair<string, object?>(MessagingTags.System, MessagingTags.SystemValue),
            new KeyValuePair<string, object?>(MessagingTags.MessageType, messageType),
            new KeyValuePair<string, object?>(MessagingTags.Outcome, outcome));
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
