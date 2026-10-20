using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace MessagingKit.IntegrationTests.Infrastructure;

public sealed record RecordedMeasurement(string Instrument, string? Unit, double Value, IReadOnlyDictionary<string, object?> Tags)
{
    public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
}

/// <summary>Captures every measurement published on the MessagingKit meter while it is alive.</summary>
public sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<RecordedMeasurement> _measurements = new();

    public MetricRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MessagingDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.Start();
    }

    /// <summary>Measurements on <paramref name="instrument"/> for one message type, so stray traffic cannot leak in.</summary>
    public IReadOnlyList<RecordedMeasurement> For(string instrument, string messageType) =>
        _measurements
            .Where(m => m.Instrument == instrument && Equals(m.Tag("messaging.message.type"), messageType))
            .ToList();

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(tags.Length);

        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        _measurements.Enqueue(new RecordedMeasurement(instrument.Name, instrument.Unit, value, copy));
    }
}
