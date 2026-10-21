using System.Diagnostics;

namespace MediatingKit;

/// <summary>
/// Names of the <see cref="ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/> MediatingKit reports
/// to. Every send and publish is instrumented on its own; a host only has to listen. With nobody listening the cost
/// is one check per call.
/// </summary>
/// <example>
/// With the OpenTelemetry SDK:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(MediatingKitInstrumentation.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(MediatingKitInstrumentation.MeterName));
/// </code>
/// </example>
public static class MediatingKitInstrumentation
{
    /// <summary>The name of the <see cref="ActivitySource"/> that emits one span per send or publish.</summary>
    public const string ActivitySourceName = "MediatingKit";

    /// <summary>The name of the meter that records dispatch counts and durations.</summary>
    public const string MeterName = "MediatingKit";

    internal static readonly string Version =
        typeof(MediatingKitInstrumentation).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
}
