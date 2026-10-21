using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace MediatingKit.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class TelemetryTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task A_send_emits_a_span_and_a_success_measurement()
    {
        await using var host = TestHost.Create();
        using var spans = new SpanRecorder();
        using var dispatches = Collector<long>(host, MediatorTelemetry.DispatchesName);
        using var durations = Collector<double>(host, MediatorTelemetry.DispatchDurationName);

        await host.Mediator.Send(new DeleteFile("secret-id"), Ct);

        var span = spans.Single();
        Assert.AreEqual("send DeleteFile", span.DisplayName);
        Assert.AreEqual(ActivityKind.Internal, span.Kind);
        Assert.AreEqual("send", span.GetTagItem("mediatingkit.operation"));
        Assert.AreEqual("MediatingKit.UnitTests.DeleteFile", span.GetTagItem("mediatingkit.message.type"));
        Assert.AreEqual("success", span.GetTagItem("mediatingkit.outcome"));
        Assert.IsFalse(span.TagObjects.Any(t => t.Value?.ToString()?.Contains("secret-id", StringComparison.Ordinal) == true), "request data is never recorded");

        var measurement = dispatches.GetMeasurementSnapshot().Single();
        Assert.AreEqual(1, measurement.Value);
        Assert.AreEqual("send", measurement.Tags["mediatingkit.operation"]);
        Assert.AreEqual("MediatingKit.UnitTests.DeleteFile", measurement.Tags["mediatingkit.message.type"]);
        Assert.AreEqual("success", measurement.Tags["mediatingkit.outcome"]);
        Assert.ContainsSingle(durations.GetMeasurementSnapshot());
    }

    [TestMethod]
    public async Task A_failed_result_is_a_successful_dispatch()
    {
        // The mediator does not interpret responses: a handler that returned a failed Result did its job.
        await using var host = TestHost.Create();
        using var spans = new SpanRecorder();

        await host.Mediator.Send(new DeleteFile("missing"), Ct);

        Assert.AreEqual("success", spans.Single().GetTagItem("mediatingkit.outcome"));
    }

    [TestMethod]
    public async Task An_exception_is_recorded_by_type_only()
    {
        await using var host = TestHost.Create();
        using var spans = new SpanRecorder();
        using var dispatches = Collector<long>(host, MediatorTelemetry.DispatchesName);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Mediator.Send(new Explode(true), Ct));

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.IsNull(span.StatusDescription, "messages can carry request data");
        Assert.AreEqual("failure", span.GetTagItem("mediatingkit.outcome"));
        Assert.AreEqual("System.InvalidOperationException", span.GetTagItem("error.type"));
        Assert.AreEqual("System.InvalidOperationException", dispatches.GetMeasurementSnapshot().Single().Tags["error.type"]);
    }

    [TestMethod]
    public async Task A_publish_emits_one_span_for_all_handlers()
    {
        await using var host = TestHost.Create();
        using var spans = new SpanRecorder();

        await host.Mediator.Publish(new FileDeleted("f"), Ct);

        var span = spans.Single();
        Assert.AreEqual("publish FileDeleted", span.DisplayName);
        Assert.AreEqual("publish", span.GetTagItem("mediatingkit.operation"));
    }

    [TestMethod]
    public async Task The_span_is_the_parent_of_work_done_in_the_handler_and_does_not_leak_to_the_caller()
    {
        await using var host = TestHost.Create(o => o.AddBehavior(typeof(TraceBehavior<,>)));
        using var spans = new SpanRecorder();
        var before = Activity.Current;

        await host.Mediator.Send(new GetFile("a"), Ct);

        Assert.AreSame(before, Activity.Current);
        Assert.AreEqual("send GetFile", spans.Single().DisplayName);
    }

    [TestMethod]
    public async Task Nothing_is_recorded_when_nobody_listens()
    {
        await using var host = TestHost.Create();
        var before = Activity.Current;

        await host.Mediator.Send(new Ping(1), Ct);

        Assert.AreSame(before, Activity.Current);
    }

    private static MetricCollector<T> Collector<T>(TestHost host, string instrument)
        where T : struct =>
        new(host.Root.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), MediatingKitInstrumentation.MeterName, instrument);

    private sealed class SpanRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _spans = [];

        public SpanRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == MediatingKitInstrumentation.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_spans)
                    {
                        _spans.Add(activity);
                    }
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public Activity Single()
        {
            lock (_spans)
            {
                return _spans.Single();
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
