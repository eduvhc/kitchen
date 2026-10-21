using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LockingKit;

/// <summary>LockingKit's instruments, on a meter owned by the host's <see cref="IMeterFactory"/>.</summary>
internal sealed class LockingKitMetrics(IMeterFactory meterFactory)
{
    public const string AcquiresName = "lockingkit.acquires";
    public const string AcquireDurationName = "lockingkit.acquire.duration";
    public const string HeldName = "lockingkit.held";
    public const string HoldDurationName = "lockingkit.hold.duration";
    public const string RenewalsName = "lockingkit.renewals";
    public const string LossesName = "lockingkit.losses";
    public const string ReleasesName = "lockingkit.releases";

    // The OpenTelemetry semconv boundaries for durations in seconds, extended for waits and holds that run to minutes.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10, 30, 60, 300];

    private readonly Instruments _instruments = new((meterFactory ?? throw new ArgumentNullException(nameof(meterFactory)))
        .Create(new MeterOptions(LockingKitInstrumentation.MeterName) { Version = LockingKitInstrumentation.Version }));

    public void Acquire(string name, string provider, string operation, string outcome, string? errorType, string? errorKind, TimeSpan duration)
    {
        if (!_instruments.Acquires.Enabled && !_instruments.AcquireDuration.Enabled)
        {
            return;
        }

        var tags = Base(name, provider);
        tags.Add(LockingKitTags.Operation, operation);
        tags.Add(LockingKitTags.Outcome, outcome);

        if (errorType is not null)
        {
            tags.Add(LockingKitTags.ErrorType, errorType);
        }

        if (errorKind is not null)
        {
            tags.Add(LockingKitTags.ErrorKind, errorKind);
        }

        _instruments.Acquires.Add(1, tags);
        _instruments.AcquireDuration.Record(duration.TotalSeconds, tags);
    }

    public void Held(string name, string provider, int delta) => _instruments.Held.Add(delta, Base(name, provider));

    public void HoldEnded(string name, string provider, TimeSpan duration) => _instruments.HoldDuration.Record(duration.TotalSeconds, Base(name, provider));

    public void Renewal(string name, string provider, bool success)
    {
        var tags = Base(name, provider);
        tags.Add(LockingKitTags.Outcome, success ? LockingKitTags.Success : LockingKitTags.Failure);
        _instruments.Renewals.Add(1, tags);
    }

    public void Lost(string name, string provider, LockLossReason reason)
    {
        var tags = Base(name, provider);
        tags.Add(LockingKitTags.LossReason, LockingKitTags.ToTagValue(reason));
        _instruments.Losses.Add(1, tags);
    }

    public void Released(string name, string provider, string? errorType)
    {
        var tags = Base(name, provider);
        tags.Add(LockingKitTags.Outcome, errorType is null ? LockingKitTags.Success : LockingKitTags.Failure);

        if (errorType is not null)
        {
            tags.Add(LockingKitTags.ErrorType, errorType);
        }

        _instruments.Releases.Add(1, tags);
    }

    // Every instrument comes from one meter, created once through the host's factory.
    private sealed class Instruments(Meter meter)
    {
        public Counter<long> Acquires { get; } = meter.CreateCounter<long>(AcquiresName, "{acquire}", "Lock acquisition calls, by outcome.");

        public Histogram<double> AcquireDuration { get; } = meter.CreateHistogram(
            AcquireDurationName,
            "s",
            "Time from calling acquire to its outcome, waiting included.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

        public UpDownCounter<long> Held { get; } = meter.CreateUpDownCounter<long>(HeldName, "{lock}", "Locks currently held by this process.");

        public Histogram<double> HoldDuration { get; } = meter.CreateHistogram(
            HoldDurationName,
            "s",
            "How long a lock was held, until it was released or lost.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

        public Counter<long> Renewals { get; } = meter.CreateCounter<long>(RenewalsName, "{renewal}", "Lease renewals and connection checks, by outcome.");

        public Counter<long> Losses { get; } = meter.CreateCounter<long>(LossesName, "{loss}", "Locks lost while held, by reason.");

        public Counter<long> Releases { get; } = meter.CreateCounter<long>(ReleasesName, "{release}", "Releases on dispose, by outcome.");
    }

    private static TagList Base(string name, string provider) => new()
    {
        { LockingKitTags.Lock, name },
        { LockingKitTags.Provider, provider },
    };
}
