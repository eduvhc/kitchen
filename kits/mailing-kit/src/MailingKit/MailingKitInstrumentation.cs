using System.Diagnostics;

namespace MailingKit;

/// <summary>
/// Names of the <see cref="ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/> MailingKit
/// reports to. MailingKit instruments every email send on its own; a host only has to listen.
/// </summary>
/// <example>
/// With the OpenTelemetry SDK:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(MailingKitInstrumentation.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(MailingKitInstrumentation.MeterName));
/// </code>
/// </example>
public static class MailingKitInstrumentation
{
    /// <summary>The name of the <see cref="ActivitySource"/> that emits one span per email send.</summary>
    public const string ActivitySourceName = "MailingKit";

    /// <summary>The name of the meter that records email send counts and durations.</summary>
    public const string MeterName = "MailingKit";

    internal static readonly string Version =
        typeof(MailingKitInstrumentation).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
}
