using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StoringKit;

/// <summary>StoringKit's instruments, on a meter owned by the host's <see cref="IMeterFactory"/>.</summary>
internal sealed class StoringKitMetrics
{
    public const string OperationsName = "storingkit.client.operations";
    public const string OperationDurationName = "storingkit.client.operation.duration";

    // The OpenTelemetry semconv boundaries for durations in seconds; the SDK defaults are sized for milliseconds.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private readonly Counter<long> _operations;
    private readonly Histogram<double> _duration;

    public StoringKitMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(StoringKitInstrumentation.MeterName)
        {
            Version = StoringKitInstrumentation.Version,
        });

        _operations = meter.CreateCounter<long>(
            OperationsName,
            "{operation}",
            "Object store operations, by outcome.");

        _duration = meter.CreateHistogram(
            OperationDurationName,
            "s",
            "Duration of one object store operation, up to the response headers for a get.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
    }

    public void Record(
        string store,
        string provider,
        string operation,
        string outcome,
        string? errorType,
        string? errorKind,
        TimeSpan duration)
    {
        if (!_operations.Enabled && !_duration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { StoringKitTags.Store, store },
            { StoringKitTags.Provider, provider },
            { StoringKitTags.Operation, operation },
            { StoringKitTags.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(StoringKitTags.ErrorType, errorType);
        }

        if (errorKind is not null)
        {
            tags.Add(StoringKitTags.ErrorKind, errorKind);
        }

        _operations.Add(1, tags);
        _duration.Record(duration.TotalSeconds, tags);
    }
}
