using System.Diagnostics;

namespace LockingKit;

/// <summary>
/// Names of the <see cref="ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/> LockingKit reports
/// to. Every lock provider registered through <c>AddLocking</c> is instrumented on its own; a host only has to listen.
/// </summary>
/// <example>
/// With the OpenTelemetry SDK:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(LockingKitInstrumentation.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(LockingKitInstrumentation.MeterName));
/// </code>
/// </example>
public static class LockingKitInstrumentation
{
    /// <summary>The name of the <see cref="ActivitySource"/> that emits one span per acquire and per release.</summary>
    public const string ActivitySourceName = "LockingKit";

    /// <summary>The name of the meter that records acquisitions, holds, renewals, losses and releases.</summary>
    public const string MeterName = "LockingKit";

    internal static readonly string Version =
        typeof(LockingKitInstrumentation).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
}
