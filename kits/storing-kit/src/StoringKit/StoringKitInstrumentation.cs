using System.Diagnostics;

namespace StoringKit;

/// <summary>
/// Names of the <see cref="ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/> StoringKit reports
/// to. Every store registered through <c>AddObjectStore</c> — and so every provider's own registration — is
/// instrumented on its own; a host only has to listen.
/// </summary>
/// <example>
/// With the OpenTelemetry SDK:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(StoringKitInstrumentation.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(StoringKitInstrumentation.MeterName));
/// </code>
/// </example>
public static class StoringKitInstrumentation
{
    /// <summary>The name of the <see cref="ActivitySource"/> that emits one span per store operation.</summary>
    public const string ActivitySourceName = "StoringKit";

    /// <summary>The name of the meter that records store operation counts and durations.</summary>
    public const string MeterName = "StoringKit";

    internal static readonly string Version =
        typeof(StoringKitInstrumentation).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
}
