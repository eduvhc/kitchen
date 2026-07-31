using System.Diagnostics;

namespace MessagingKit;

/// <summary>
/// Carries W3C trace context across the outbox → transport → inbox hop, so a send and the handling
/// that follows it land in one trace instead of two unrelated ones.
/// </summary>
public static class MessagingDiagnostics
{
    public const string ActivitySourceName = "MessagingKit";

    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

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
