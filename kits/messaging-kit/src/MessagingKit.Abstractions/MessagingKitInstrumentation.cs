namespace MessagingKit;

/// <summary>
/// The names MessagingKit publishes its telemetry under. A host opts in by subscribing to both:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(MessagingKitInstrumentation.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(MessagingKitInstrumentation.MeterName));
/// </code>
/// </summary>
/// <remarks>
/// Spans come from a process-wide <see cref="System.Diagnostics.ActivitySource"/>; metrics come from a
/// <see cref="System.Diagnostics.Metrics.Meter"/> created through the container's
/// <see cref="System.Diagnostics.Metrics.IMeterFactory"/>, so each service provider gets its own.
/// </remarks>
public static class MessagingKitInstrumentation
{
    /// <summary>The name of the activity source the outbox dispatcher and inbox processor start spans on.</summary>
    public const string ActivitySourceName = "MessagingKit";

    /// <summary>The name of the meter the outbox and inbox report their instruments on.</summary>
    public const string MeterName = "MessagingKit";
}
