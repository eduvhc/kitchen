using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace LockingKit.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class InstrumentationTests
{
    private const string SecretKey = "tenants/ada@example.com/export";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task An_acquisition_emits_a_client_span_and_an_acquired_measurement()
    {
        var (locks, meters, _) = Build();
        await using var _ = locks;
        using var spans = new SpanRecorder();
        using var acquires = Collector<long>(meters, LockingKitMetrics.AcquiresName);
        using var durations = Collector<double>(meters, LockingKitMetrics.AcquireDurationName);
        using var held = Collector<long>(meters, LockingKitMetrics.HeldName);

        var handle = (await locks.TryAcquireAsync(SecretKey, TestContext.CancellationToken)).Value;

        var span = spans.Single();
        Assert.AreEqual("try_acquire Default", span.DisplayName);
        Assert.AreEqual(ActivityKind.Client, span.Kind);
        Assert.AreEqual(ActivityStatusCode.Unset, span.Status);
        Assert.AreEqual("Default", span.GetTagItem("lockingkit.lock"));
        Assert.AreEqual("scripted", span.GetTagItem("lockingkit.provider"));
        Assert.AreEqual("try_acquire", span.GetTagItem("lockingkit.operation"));
        Assert.AreEqual("acquired", span.GetTagItem("lockingkit.outcome"));
        Assert.AreEqual(1, span.GetTagItem("lockingkit.acquire.attempts"));
        Assert.AreEqual(1L, span.GetTagItem("lockingkit.fencing_token"));

        var expected = new Dictionary<string, object?>
        {
            ["lockingkit.lock"] = "Default",
            ["lockingkit.provider"] = "scripted",
            ["lockingkit.operation"] = "try_acquire",
            ["lockingkit.outcome"] = "acquired",
        };
        AssertTags(acquires.GetMeasurementSnapshot().Single().Tags, expected);
        AssertTags(durations.GetMeasurementSnapshot().Single().Tags, expected);
        Assert.AreEqual(1, held.GetMeasurementSnapshot().Sum(m => m.Value));

        await handle.DisposeAsync();
        Assert.AreEqual(0, held.GetMeasurementSnapshot().Sum(m => m.Value));
    }

    [TestMethod]
    public async Task Contention_is_recorded_as_an_outcome_not_an_error()
    {
        var (locks, meters, backend) = Build();
        await using var _ = locks;
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        using var spans = new SpanRecorder();
        using var acquires = Collector<long>(meters, LockingKitMetrics.AcquiresName);

        await locks.TryAcquireAsync(SecretKey, TestContext.CancellationToken);

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Unset, span.Status);
        Assert.AreEqual("busy", span.GetTagItem("lockingkit.outcome"));
        Assert.IsNull(span.GetTagItem("error.type"));
        AssertTags(acquires.GetMeasurementSnapshot().Single().Tags, new()
        {
            ["lockingkit.lock"] = "Default",
            ["lockingkit.provider"] = "scripted",
            ["lockingkit.operation"] = "try_acquire",
            ["lockingkit.outcome"] = "busy",
        });
    }

    [TestMethod]
    public async Task A_backend_failure_is_recorded_with_its_code_and_kind()
    {
        var (locks, meters, backend) = Build();
        await using var _ = locks;
        backend.OnAcquire = (_, _) => throw new TimeoutException($"timed out for {SecretKey}");
        using var spans = new SpanRecorder();
        using var acquires = Collector<long>(meters, LockingKitMetrics.AcquiresName);

        await locks.TryAcquireAsync(SecretKey, TestContext.CancellationToken);

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual(LockingErrors.Unavailable, span.StatusDescription);
        Assert.AreEqual(LockingErrors.Unavailable, span.GetTagItem("error.type"));
        Assert.AreEqual("transient", span.GetTagItem("lockingkit.error.kind"));
        AssertTags(acquires.GetMeasurementSnapshot().Single().Tags, new()
        {
            ["lockingkit.lock"] = "Default",
            ["lockingkit.provider"] = "scripted",
            ["lockingkit.operation"] = "try_acquire",
            ["lockingkit.outcome"] = "failure",
            ["error.type"] = LockingErrors.Unavailable,
            ["lockingkit.error.kind"] = "transient",
        });
    }

    [TestMethod]
    public async Task An_exception_is_recorded_by_type_without_its_message_and_rethrown()
    {
        var (locks, meters, backend) = Build();
        await using var _ = locks;
        backend.OnAcquire = (_, _) => throw new UnauthorizedAccessException($"denied for {SecretKey}");
        using var spans = new SpanRecorder();
        using var acquires = Collector<long>(meters, LockingKitMetrics.AcquiresName);

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => locks.TryAcquireAsync(SecretKey, TestContext.CancellationToken));

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("System.UnauthorizedAccessException", span.GetTagItem("error.type"));
        Assert.IsEmpty(span.Events, "Exception events carry messages, which can carry the key.");
        Assert.AreEqual("System.UnauthorizedAccessException", acquires.GetMeasurementSnapshot().Single().Tags["error.type"]);
    }

    [TestMethod]
    public async Task Waiting_records_its_attempts_and_a_timeout_outcome()
    {
        var (locks, meters, backend) = Build();
        await using var _ = locks;
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        using var spans = new SpanRecorder();
        using var acquires = Collector<long>(meters, LockingKitMetrics.AcquiresName);

        await locks.AcquireAsync(SecretKey, TimeSpan.FromMilliseconds(300), TestContext.CancellationToken);

        var span = spans.Single();
        Assert.AreEqual("acquire Default", span.DisplayName);
        Assert.AreEqual("timeout", span.GetTagItem("lockingkit.outcome"));
        Assert.IsGreaterThan(1, (int)span.GetTagItem("lockingkit.acquire.attempts")!);
        Assert.AreEqual("timeout", acquires.GetMeasurementSnapshot().Single().Tags["lockingkit.outcome"]);
    }

    [TestMethod]
    public async Task Renewals_losses_holds_and_releases_are_measured()
    {
        var time = Engines.Clock();
        var (locks, meters, backend) = Build(time);
        await using var _ = locks;
        using var renewals = Collector<long>(meters, LockingKitMetrics.RenewalsName);
        using var losses = Collector<long>(meters, LockingKitMetrics.LossesName);
        using var holds = Collector<double>(meters, LockingKitMetrics.HoldDurationName);
        using var releases = Collector<long>(meters, LockingKitMetrics.ReleasesName);
        using var held = Collector<long>(meters, LockingKitMetrics.HeldName);

        var handle = await locks.HoldAsync(SecretKey);
        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await Engines.UntilAsync(() => renewals.GetMeasurementSnapshot().Count == 1, "one renewal");

        backend.OnRenew = _ => Task.FromResult(false);
        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await Engines.UntilAsync(() => handle.IsLost, "the lock was revoked");
        await handle.DisposeAsync();

        CollectionAssert.AreEqual(
            new[] { "success", "failure" },
            renewals.GetMeasurementSnapshot().Select(m => m.Tags["lockingkit.outcome"]).ToArray());
        Assert.AreEqual("revoked", losses.GetMeasurementSnapshot().Single().Tags["lockingkit.loss.reason"]);
        Assert.AreEqual(20, holds.GetMeasurementSnapshot().Single().Value, 0.001);
        Assert.AreEqual("success", releases.GetMeasurementSnapshot().Single().Tags["lockingkit.outcome"]);
        Assert.AreEqual(0, held.GetMeasurementSnapshot().Sum(m => m.Value), "A loss ends the hold once; the dispose after it does not count again.");
    }

    [TestMethod]
    public async Task A_failed_release_is_measured_by_exception_type_and_spanned()
    {
        var (locks, meters, backend) = Build();
        await using var _ = locks;
        backend.OnRelease = _ => throw new IOException("reset");
        using var releases = Collector<long>(meters, LockingKitMetrics.ReleasesName);
        var handle = await locks.HoldAsync(SecretKey);
        using var spans = new SpanRecorder();

        await handle.DisposeAsync();

        var span = spans.Single();
        Assert.AreEqual("release Default", span.DisplayName);
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        AssertTags(releases.GetMeasurementSnapshot().Single().Tags, new()
        {
            ["lockingkit.lock"] = "Default",
            ["lockingkit.provider"] = "scripted",
            ["lockingkit.outcome"] = "failure",
            ["error.type"] = "System.IO.IOException",
        });
    }

    [TestMethod]
    public async Task No_span_or_measurement_ever_records_the_key()
    {
        var time = Engines.Clock();
        var (locks, meters, backend) = Build(time);
        await using var _ = locks;
        using var spans = new SpanRecorder();
        var recorded = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == LockingKitInstrumentation.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => Record(tags));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => Record(tags));
        listener.Start();

        var handle = await locks.HoldAsync(SecretKey);
        backend.OnAcquire = (_, _) => Task.FromResult<ILockLease?>(null);
        await locks.TryAcquireAsync(SecretKey, TestContext.CancellationToken);
        await time.AdvanceAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        await Engines.UntilAsync(() => backend.Renewals == 1, "a renewal");
        await handle.DisposeAsync();

        Assert.IsGreaterThan(5, recorded.Count);
        Assert.IsTrue(recorded.All(v => !v.Contains("ada", StringComparison.Ordinal)), string.Join(", ", recorded));

        foreach (var span in spans.Recorded)
        {
            Assert.DoesNotContain("ada", span.DisplayName);

            foreach (var (name, value) in span.TagObjects)
            {
                Assert.DoesNotContain("ada", value?.ToString() ?? string.Empty, $"{span.DisplayName} records {name}");
            }
        }

        void Record(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (recorded)
            {
                foreach (var tag in tags)
                {
                    recorded.Add($"{tag.Key}={tag.Value}");
                }
            }
        }
    }

    private static (DistributedLock Locks, IMeterFactory Meters, ScriptedBackend Backend) Build(TimeProvider? time = null)
    {
        var meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
        var backend = ScriptedBackend.Expiring(TimeSpan.FromSeconds(30));
        var locks = Engines.Create(
            backend,
            time ?? TimeProvider.System,
            new TestLockingOptions { MinRetryDelay = TimeSpan.FromMilliseconds(20), MaxRetryDelay = TimeSpan.FromMilliseconds(50) },
            meters: meters);
        return (locks, meters, backend);
    }

    private static MetricCollector<T> Collector<T>(IMeterFactory meters, string instrument)
        where T : struct =>
        new(meters, LockingKitInstrumentation.MeterName, instrument);

    private static void AssertTags(IReadOnlyDictionary<string, object?> actual, Dictionary<string, object?> expected)
    {
        Assert.HasCount(expected.Count, actual, string.Join(", ", actual.Select(t => $"{t.Key}={t.Value}")));

        foreach (var (name, value) in expected)
        {
            Assert.AreEqual(value, actual[name], name);
        }
    }

    private sealed class SpanRecorder : IDisposable
    {
        private readonly ActivityListener _listener;

        public SpanRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == LockingKitInstrumentation.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (Recorded)
                    {
                        Recorded.Add(activity);
                    }
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Recorded { get; } = [];

        public Activity Single() => Recorded.Single();

        public void Dispose() => _listener.Dispose();
    }
}
