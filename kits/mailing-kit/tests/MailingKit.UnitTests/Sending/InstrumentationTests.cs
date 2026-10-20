using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailingKit.Sending;
using MessagingKit.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResultingKit;

namespace MailingKit.UnitTests.Sending;

[TestClass]
public sealed class InstrumentationTests
{
    private static readonly ActivitySource TestSource = new("MailingKit.UnitTests");

    private readonly FakeEmailSender _sender = new();

    [TestMethod]
    public async Task A_successful_send_emits_a_client_span_and_a_sent_measurement()
    {
        await using var provider = BuildProvider(transportFirst: true);
        using var spans = new SpanRecorder();
        using var metrics = new MetricRecorder(provider);

        await HandleAsync(provider, spans, new SendEmail
        {
            To = ["ada@example.com", "grace@example.com"],
            Cc = ["alan@example.com"],
            Subject = "Hello",
            Text = "Hello",
            Attachments = [new Attachment { FileName = "a.txt", ContentType = "text/plain", Content = "aGk=" }],
        });

        var span = spans.Single();
        Assert.AreEqual("send email", span.DisplayName);
        Assert.AreEqual(ActivityKind.Client, span.Kind);
        Assert.AreEqual(ActivityStatusCode.Unset, span.Status);
        Assert.AreEqual("fake", span.GetTagItem("mailingkit.provider"));
        Assert.AreEqual(3, span.GetTagItem("mailingkit.email.recipient_count"));
        Assert.AreEqual(1, span.GetTagItem("mailingkit.email.attachment_count"));
        Assert.AreEqual("sent", span.GetTagItem("mailingkit.outcome"));
        Assert.AreEqual("provider-1", span.GetTagItem("mailingkit.email.provider_message_id"));
        Assert.IsNull(span.GetTagItem("error.type"));

        var expected = new Dictionary<string, object?>
        {
            ["mailingkit.provider"] = "fake",
            ["mailingkit.outcome"] = "sent",
        };
        metrics.AssertSingle(expected);
    }

    [TestMethod]
    public async Task A_permanent_failure_is_reported_with_its_code_and_kind()
    {
        _sender.Behaviour = _ => Error.Validation("fake.rejected", "ada@example.com is not a mailbox");

        await using var provider = BuildProvider(transportFirst: true);
        using var spans = new SpanRecorder();
        using var metrics = new MetricRecorder(provider);

        await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(provider, spans, Message()));

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("fake.rejected", span.StatusDescription);
        Assert.AreEqual("failed", span.GetTagItem("mailingkit.outcome"));
        Assert.AreEqual("fake.rejected", span.GetTagItem("error.type"));
        Assert.AreEqual("validation", span.GetTagItem("mailingkit.error.kind"));
        Assert.IsNull(span.GetTagItem("mailingkit.email.provider_message_id"));

        metrics.AssertSingle(new Dictionary<string, object?>
        {
            ["mailingkit.provider"] = "fake",
            ["mailingkit.outcome"] = "failed",
            ["error.type"] = "fake.rejected",
            ["mailingkit.error.kind"] = "validation",
        });
    }

    [TestMethod]
    public async Task A_transient_failure_is_reported_as_transient()
    {
        _sender.Behaviour = _ => Error.Transient("fake.unavailable", "try later");

        await using var provider = BuildProvider(transportFirst: true);
        using var spans = new SpanRecorder();
        using var metrics = new MetricRecorder(provider);

        await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(provider, spans, Message()));

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("failed", span.GetTagItem("mailingkit.outcome"));
        Assert.AreEqual("fake.unavailable", span.GetTagItem("error.type"));
        Assert.AreEqual("transient", span.GetTagItem("mailingkit.error.kind"));

        metrics.AssertSingle(new Dictionary<string, object?>
        {
            ["mailingkit.provider"] = "fake",
            ["mailingkit.outcome"] = "failed",
            ["error.type"] = "fake.unavailable",
            ["mailingkit.error.kind"] = "transient",
        });
    }

    [TestMethod]
    public async Task An_exception_is_recorded_and_rethrown_unchanged()
    {
        var thrown = new InvalidOperationException("boom");
        _sender.Behaviour = _ => throw thrown;

        await using var provider = BuildProvider(transportFirst: true);
        using var spans = new SpanRecorder();
        using var metrics = new MetricRecorder(provider);

        var caught = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => HandleAsync(provider, spans, Message()));
        Assert.AreSame(thrown, caught);

        var span = spans.Single();
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("failed", span.GetTagItem("mailingkit.outcome"));
        Assert.AreEqual("System.InvalidOperationException", span.GetTagItem("error.type"));
        Assert.IsNull(span.GetTagItem("mailingkit.error.kind"));

        var exception = span.Events.Single(e => e.Name == "exception");
        Assert.AreEqual("System.InvalidOperationException", exception.Tags.Single(t => t.Key == "exception.type").Value);

        metrics.AssertSingle(new Dictionary<string, object?>
        {
            ["mailingkit.provider"] = "fake",
            ["mailingkit.outcome"] = "failed",
            ["error.type"] = "System.InvalidOperationException",
        });
    }

    [TestMethod]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _sender.Behaviour = _ => throw new OperationCanceledException(cts.Token);

        await using var provider = BuildProvider(transportFirst: true);
        using var spans = new SpanRecorder();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => HandleAsync(provider, spans, Message(), cts.Token));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task The_sender_is_instrumented_whether_the_transport_is_registered_before_or_after_AddMailing(bool transportFirst)
    {
        await using var provider = BuildProvider(transportFirst);
        using var spans = new SpanRecorder();
        using var metrics = new MetricRecorder(provider);

        await HandleAsync(provider, spans, Message());

        Assert.AreEqual("fake", spans.Single().GetTagItem("mailingkit.provider"));
        Assert.HasCount(1, metrics.Counts);
        Assert.HasCount(1, _sender.Sent);
    }

    [TestMethod]
    public async Task An_already_instrumented_sender_is_not_wrapped_again()
    {
        var services = new ServiceCollection();
        AddDatabase(services);
        services.AddMailing<TestDbContext>();
        services.AddScoped<IEmailSender>(sp => new InstrumentedEmailSender(_sender, sp.GetRequiredService<MailingKitMetrics>()));

        await using var provider = services.BuildServiceProvider();
        using var spans = new SpanRecorder();
        using var metrics = new MetricRecorder(provider);

        await HandleAsync(provider, spans, Message());

        Assert.HasCount(1, spans.Stopped);
        Assert.HasCount(1, metrics.Counts);
    }

    [TestMethod]
    public void The_handler_is_registered_once()
    {
        var services = new ServiceCollection();
        AddDatabase(services);
        services.AddSingleton<IEmailSender>(_sender);
        services.AddMailing<TestDbContext>();

        var handler = services.Single(d => d.ServiceType == typeof(IMessageHandler<SendEmail>));
        Assert.IsNotNull(handler.ImplementationFactory);
        Assert.AreEqual(ServiceLifetime.Scoped, handler.Lifetime);
    }

    [TestMethod]
    [DataRow(typeof(FakeEmailSender), "fake")]
    [DataRow(typeof(AcmeMailSender), "acme")]
    [DataRow(typeof(PostmarkSender), "postmark")]
    [DataRow(typeof(Courier), "courier")]
    public void The_provider_name_comes_from_the_sender_type(Type senderType, string expected) =>
        Assert.AreEqual(expected, InstrumentedEmailSender.ProviderName(senderType));

    private ServiceProvider BuildProvider(bool transportFirst)
    {
        var services = new ServiceCollection();
        AddDatabase(services);

        if (transportFirst)
        {
            services.TryAddSingleton<IEmailSender>(_sender);
        }

        services.AddMailing<TestDbContext>();

        if (!transportFirst)
        {
            services.TryAddSingleton<IEmailSender>(_sender);
        }

        return services.BuildServiceProvider();
    }

    private static void AddDatabase(IServiceCollection services)
    {
        var name = Guid.CreateVersion7().ToString();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(name));
    }

    private static async Task HandleAsync(
        IServiceProvider provider,
        SpanRecorder spans,
        SendEmail message,
        CancellationToken ct = default)
    {
        // A parent span scopes what this test sees; the other tests run in parallel on the same source.
        using var parent = TestSource.StartActivity("test")!;
        spans.TraceId = parent.TraceId;

        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<SendEmail>>();

        await handler.HandleAsync(
            message,
            new MessageContext { MessageId = Guid.CreateVersion7(), Type = "send-email", AttemptCount = 1 },
            ct);
    }

    private static SendEmail Message() => new() { To = ["ada@example.com"], Subject = "Hello", Text = "Hello" };

    private sealed class SpanRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly ConcurrentQueue<Activity> _stopped = new();

        public SpanRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name is MailingKitInstrumentation.ActivitySourceName or "MailingKit.UnitTests",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a =>
                {
                    if (a.Source.Name == MailingKitInstrumentation.ActivitySourceName)
                    {
                        _stopped.Enqueue(a);
                    }
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public ActivityTraceId TraceId { get; set; }

        public List<Activity> Stopped => [.. _stopped.Where(a => a.TraceId == TraceId)];

        public Activity Single() => Stopped.Single();

        public void Dispose() => _listener.Dispose();
    }

    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricRecorder(IServiceProvider provider)
        {
            var factory = provider.GetRequiredService<IMeterFactory>();

            // Scoped to this provider's factory, so parallel tests never see each other's measurements.
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == factory && instrument.Meter.Name == MailingKitInstrumentation.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (instrument.Name == MailingKitMetrics.SentEmailsName)
                {
                    Counts.Add((value, ToDictionary(tags)));
                }
            });

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                if (instrument.Name == MailingKitMetrics.SendDurationName)
                {
                    Durations.Add((value, ToDictionary(tags)));
                }
            });

            _listener.Start();
        }

        public ConcurrentBag<(long Value, Dictionary<string, object?> Tags)> Counts { get; } = [];

        public ConcurrentBag<(double Value, Dictionary<string, object?> Tags)> Durations { get; } = [];

        public void AssertSingle(Dictionary<string, object?> expectedTags)
        {
            var count = Counts.Single();
            Assert.AreEqual(1L, count.Value);
            CollectionAssert.AreEquivalent(expectedTags, count.Tags);

            var duration = Durations.Single();
            Assert.IsGreaterThanOrEqualTo(0d, duration.Value);
            CollectionAssert.AreEquivalent(expectedTags, duration.Tags);
        }

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>();

            foreach (var tag in tags)
            {
                result[tag.Key] = tag.Value;
            }

            return result;
        }
    }

    private sealed class AcmeMailSender;

    private sealed class PostmarkSender;

    private sealed class Courier;
}
