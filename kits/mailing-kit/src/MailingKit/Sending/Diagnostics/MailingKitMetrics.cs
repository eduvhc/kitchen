using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MailingKit.Sending;

/// <summary>MailingKit's instruments, on a meter owned by the host's <see cref="IMeterFactory"/>.</summary>
internal sealed class MailingKitMetrics
{
    public const string SentEmailsName = "mailingkit.client.sent.emails";
    public const string SendDurationName = "mailingkit.client.send.duration";

    // The OpenTelemetry semconv boundaries for durations in seconds; the SDK defaults are sized for milliseconds.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private readonly Counter<long> _sentEmails;
    private readonly Histogram<double> _sendDuration;

    public MailingKitMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(MailingKitInstrumentation.MeterName)
        {
            Version = MailingKitInstrumentation.Version,
        });

        _sentEmails = meter.CreateCounter<long>(
            SentEmailsName,
            "{email}",
            "Emails handed to a transport, by outcome.");

        _sendDuration = meter.CreateHistogram(
            SendDurationName,
            "s",
            "Duration of one email send through the transport.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
    }

    public void RecordSend(string provider, string outcome, string? errorType, string? errorKind, TimeSpan duration)
    {
        if (!_sentEmails.Enabled && !_sendDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { MailingKitTags.Provider, provider },
            { MailingKitTags.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(MailingKitTags.ErrorType, errorType);
        }

        if (errorKind is not null)
        {
            tags.Add(MailingKitTags.ErrorKind, errorKind);
        }

        _sentEmails.Add(1, tags);
        _sendDuration.Record(duration.TotalSeconds, tags);
    }
}
