using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace StoringKit.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class InstrumentationTests
{
    private const string SecretKey = "tenants/ada@example.com/passport.pdf";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task A_successful_put_emits_a_client_span_and_a_success_measurement()
    {
        await using var provider = Build(out _);
        using var spans = new SpanRecorder();
        using var operations = Collector<long>(provider, StoringKitMetrics.OperationsName);
        using var durations = Collector<double>(provider, StoringKitMetrics.OperationDurationName);

        var options = new PutObjectOptions { Condition = WriteCondition.IfNotExists };
        options.Metadata["owner"] = "ada@example.com";
        await Store(provider).PutAsync(SecretKey, new MemoryStream(Encoding.UTF8.GetBytes("12345")), options, TestContext.CancellationToken);

        var span = spans.Single();
        Assert.AreEqual("put Default", span.DisplayName);
        Assert.AreEqual(ActivityKind.Client, span.Kind);
        Assert.AreEqual(ActivityStatusCode.Unset, span.Status);
        Assert.AreEqual("Default", span.GetTagItem("storingkit.store"));
        Assert.AreEqual("fake", span.GetTagItem("storingkit.provider"));
        Assert.AreEqual("put", span.GetTagItem("storingkit.operation"));
        Assert.AreEqual("success", span.GetTagItem("storingkit.outcome"));
        Assert.AreEqual("if_not_exists", span.GetTagItem("storingkit.write.condition"));
        Assert.AreEqual(5L, span.GetTagItem("storingkit.object.size"));
        Assert.IsNull(span.GetTagItem("error.type"));

        var measurement = operations.GetMeasurementSnapshot().Single();
        Assert.AreEqual(1, measurement.Value);
        AssertTags(measurement.Tags, new()
        {
            ["storingkit.store"] = "Default",
            ["storingkit.provider"] = "fake",
            ["storingkit.operation"] = "put",
            ["storingkit.outcome"] = "success",
        });
        AssertTags(durations.GetMeasurementSnapshot().Single().Tags, new()
        {
            ["storingkit.store"] = "Default",
            ["storingkit.provider"] = "fake",
            ["storingkit.operation"] = "put",
            ["storingkit.outcome"] = "success",
        });
    }

    [TestMethod]
    public async Task An_expected_failure_is_reported_with_its_code_and_kind()
    {
        await using var provider = Build(out _);
        using var spans = new SpanRecorder();
        using var operations = Collector<long>(provider, StoringKitMetrics.OperationsName);

        var result = await Store(provider).GetAsync(SecretKey, TestContext.CancellationToken);

        Assert.IsTrue(result.IsFailure);
        var span = spans.Single();
        Assert.AreEqual("get Default", span.DisplayName);
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual(StoringErrors.NotFound, span.StatusDescription);
        Assert.AreEqual("failure", span.GetTagItem("storingkit.outcome"));
        Assert.AreEqual(StoringErrors.NotFound, span.GetTagItem("error.type"));
        Assert.AreEqual("not_found", span.GetTagItem("storingkit.error.kind"));

        AssertTags(operations.GetMeasurementSnapshot().Single().Tags, new()
        {
            ["storingkit.store"] = "Default",
            ["storingkit.provider"] = "fake",
            ["storingkit.operation"] = "get",
            ["storingkit.outcome"] = "failure",
            ["error.type"] = StoringErrors.NotFound,
            ["storingkit.error.kind"] = "not_found",
        });
    }

    [TestMethod]
    public async Task An_exception_is_reported_by_type_and_rethrown()
    {
        await using var provider = Build(out var fake);
        fake.Throw = new UnauthorizedAccessException($"denied for {SecretKey}");
        using var spans = new SpanRecorder();
        using var operations = Collector<long>(provider, StoringKitMetrics.OperationsName);

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => Store(provider).DeleteAsync(SecretKey, TestContext.CancellationToken));

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("System.UnauthorizedAccessException", span.GetTagItem("error.type"));
        Assert.IsEmpty(span.Events, "Exception events carry messages, which can carry the key.");

        AssertTags(operations.GetMeasurementSnapshot().Single().Tags, new()
        {
            ["storingkit.store"] = "Default",
            ["storingkit.provider"] = "fake",
            ["storingkit.operation"] = "delete",
            ["storingkit.outcome"] = "failure",
            ["error.type"] = "System.UnauthorizedAccessException",
        });
    }

    [TestMethod]
    public async Task Every_operation_is_traced_and_none_records_keys_or_metadata_values()
    {
        await using var provider = Build(out _);
        var store = Store(provider);
        using var spans = new SpanRecorder();
        var ct = TestContext.CancellationToken;

        var options = new PutObjectOptions();
        options.Metadata["owner"] = "ada@example.com";
        await store.PutAsync(SecretKey, new MemoryStream([1, 2, 3]), options, ct);
        (await store.GetAsync(SecretKey, ct)).Value.Dispose();
        await store.GetPropertiesAsync(SecretKey, ct);
        await store.ExistsAsync(SecretKey, ct);
        await store.ListAsync("tenants/ada@example.com/", ct: ct);
        await store.GetReadUrlAsync(SecretKey, TimeSpan.FromMinutes(1), ct);
        await store.DeleteAsync(SecretKey, ct);

        CollectionAssert.AreEqual(
            new[] { "put", "get", "get_properties", "exists", "list", "get_read_url", "delete" },
            spans.Recorded.Select(s => s.GetTagItem("storingkit.operation")).ToList());

        Assert.AreEqual(3L, spans.Recorded[1].GetTagItem("storingkit.object.size"));
        Assert.IsTrue((bool)spans.Recorded[3].GetTagItem("storingkit.object.exists")!);
        Assert.AreEqual(1, spans.Recorded[4].GetTagItem("storingkit.list.object_count"));

        foreach (var span in spans.Recorded)
        {
            Assert.DoesNotContain("ada", span.DisplayName);

            foreach (var (name, value) in span.TagObjects)
            {
                Assert.DoesNotContain("ada", value?.ToString() ?? string.Empty, $"{span.DisplayName} records {name}");
            }
        }
    }

    [TestMethod]
    public async Task Public_urls_need_no_request_so_get_no_span()
    {
        await using var provider = Build(out _, publicBaseUrl: new Uri("https://cdn.example.com/"));
        using var spans = new SpanRecorder();

        Store(provider).GetPublicUrl("a.png");

        Assert.IsEmpty(spans.Recorded);
    }

    private static ServiceProvider Build(out FakeObjectStore fake, Uri? publicBaseUrl = null)
    {
        var store = new FakeObjectStore(publicBaseUrl);
        fake = store;

        var services = new ServiceCollection();
        services.AddObjectStore(ObjectStores.DefaultName, "fake", _ => store);
        return services.BuildServiceProvider();
    }

    private static IObjectStore Store(IServiceProvider provider) => provider.GetRequiredService<IObjectStore>();

    private static MetricCollector<T> Collector<T>(IServiceProvider provider, string instrument)
        where T : struct =>
        new(provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), StoringKitInstrumentation.MeterName, instrument);

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
                ShouldListenTo = source => source.Name == StoringKitInstrumentation.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Recorded.Add,
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Recorded { get; } = [];

        public Activity Single() => Recorded.Single();

        public void Dispose() => _listener.Dispose();
    }
}
