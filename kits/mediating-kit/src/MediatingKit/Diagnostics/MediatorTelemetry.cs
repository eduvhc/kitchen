using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MediatingKit;

/// <summary>
/// MediatingKit's span and instruments, on a meter owned by the host's <see cref="IMeterFactory"/>. Request and
/// notification contents are never recorded; only the message type name, which is bounded by the code base.
/// </summary>
internal sealed class MediatorTelemetry
{
    public const string DispatchDurationName = "mediatingkit.dispatch.duration";

    // The OpenTelemetry semconv boundaries for durations in seconds, from 5 ms; in-process dispatch is usually faster,
    // so the lower end is extended down to 100 µs.
    private static readonly double[] DurationBuckets =
        [0.0001, 0.0005, 0.001, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    // One histogram and no separate counter: a histogram already counts, per attribute set, so the dispatch count
    // is its count. It also keeps the mediator's per-call listener check to two reads.
    private readonly Histogram<double> _duration;

    public MediatorTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(MediatingKitInstrumentation.MeterName)
        {
            Version = MediatingKitInstrumentation.Version,
        });

        _duration = meter.CreateHistogram(
            DispatchDurationName,
            "s",
            "Duration of one send through its behaviors and handler, or one publish to every handler.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
    }

    /// <summary>The histogram, which the mediator checks for listeners directly on every call.</summary>
    public Histogram<double> Duration => _duration;

    public Operation Start(string operation, string spanName, string messageType)
    {
        var activity = MediatingKitInstrumentation.ActivitySource.StartActivity(spanName, ActivityKind.Internal);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(MediatingKitTags.Operation, operation);
            activity.SetTag(MediatingKitTags.MessageType, messageType);
        }

        return new Operation(this, activity, operation, messageType, Stopwatch.GetTimestamp());
    }

    private void Record(string operation, string messageType, string outcome, string? errorType, TimeSpan duration)
    {
        if (!_duration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { MediatingKitTags.Operation, operation },
            { MediatingKitTags.MessageType, messageType },
            { MediatingKitTags.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(MediatingKitTags.ErrorType, errorType);
        }

        _duration.Record(duration.TotalSeconds, tags);
    }

    /// <summary>One send or publish in flight.</summary>
    internal readonly struct Operation
    {
        private readonly MediatorTelemetry _telemetry;
        private readonly Activity? _activity;
        private readonly string _operation;
        private readonly string _messageType;
        private readonly long _start;

        public Operation(MediatorTelemetry telemetry, Activity? activity, string operation, string messageType, long start)
        {
            _telemetry = telemetry;
            _activity = activity;
            _operation = operation;
            _messageType = messageType;
            _start = start;
        }

        public void Succeed()
        {
            if (_activity is not null)
            {
                _activity.SetTag(MediatingKitTags.Outcome, MediatingKitTags.Success);
                _activity.Dispose();
            }

            _telemetry.Record(_operation, _messageType, MediatingKitTags.Success, errorType: null, Stopwatch.GetElapsedTime(_start));
        }

        public void Fail(Exception exception)
        {
            var errorType = exception.GetType().FullName ?? exception.GetType().Name;

            if (_activity is not null)
            {
                _activity.SetTag(MediatingKitTags.Outcome, MediatingKitTags.Failure);
                _activity.SetTag(MediatingKitTags.ErrorType, errorType);

                // The type, not the message: messages routinely carry the request's data.
                _activity.SetStatus(ActivityStatusCode.Error);
                _activity.Dispose();
            }

            _telemetry.Record(_operation, _messageType, MediatingKitTags.Failure, errorType, Stopwatch.GetElapsedTime(_start));
        }
    }
}
