using System.ComponentModel;
using System.Diagnostics;

namespace MessagingKit.Abstractions;

/// <summary>
/// Carries W3C trace context across the outbox → transport → inbox hop, so a send and the handling
/// that follows it land in one trace instead of two unrelated ones.
/// </summary>
/// <remarks>
/// The names to subscribe to live on <see cref="MessagingKitInstrumentation"/>.
/// </remarks>
public static class MessagingDiagnostics
{
    /// <summary>Kept for 0.2.x callers; the same value as <see cref="MessagingKitInstrumentation.ActivitySourceName"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public const string ActivitySourceName = MessagingKitInstrumentation.ActivitySourceName;

    /// <summary>The header the W3C <c>traceparent</c> of the staging caller is written to.</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>The header the W3C <c>tracestate</c> of the staging caller is written to, when there is one.</summary>
    public const string TraceStateHeader = "tracestate";

    /// <summary>
    /// The source every MessagingKit span starts on. Static and process-wide, like the BCL's own sources;
    /// listeners subscribe by <see cref="MessagingKitInstrumentation.ActivitySourceName"/>.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(MessagingKitInstrumentation.ActivitySourceName);

    /// <summary>
    /// Writes the ambient trace context into the message headers. Called when the message is staged,
    /// not when it is delivered — the producer's trace is the one worth linking to.
    /// </summary>
    public static void InjectTraceContext(IDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var activity = Activity.Current;

        if (activity?.Id is null || activity.IdFormat != ActivityIdFormat.W3C)
        {
            return;
        }

        headers[TraceParentHeader] = activity.Id;

        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            headers[TraceStateHeader] = activity.TraceStateString;
        }
    }

    /// <summary>Starts an activity parented to the trace context carried in the headers, if any.</summary>
    public static Activity? StartActivity(
        string name,
        ActivityKind kind,
        IReadOnlyDictionary<string, string>? headers)
    {
        var parentId = headers is not null && headers.TryGetValue(TraceParentHeader, out var traceParent)
            ? traceParent
            : null;

        var activity = parentId is null
            ? ActivitySource.StartActivity(name, kind)
            : ActivitySource.StartActivity(name, kind, parentId);

        if (activity is not null
            && headers is not null
            && headers.TryGetValue(TraceStateHeader, out var traceState))
        {
            activity.TraceStateString = traceState;
        }

        return activity;
    }
}
