using System.Collections.Concurrent;
using System.Diagnostics;
using MessagingKit.Inbox;
using MessagingKit.Inbox.Abstractions;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.Outbox;
using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResultingKit;

namespace MessagingKit.IntegrationTests;

[TestClass]
public sealed class TracingTests : MessagingTest, IDisposable
{
    private const string Type = "send-email";

    // Names an earlier version emitted, retired by the current messaging conventions.
    private static readonly string[] RetiredNames = ["messaging.operation", "messaging.attempt", "messaging.message.type"];

    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public TracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingKitInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [TestMethod]
    public async Task A_send_is_a_client_span_in_the_callers_trace()
    {
        ActivityTraceId callerTrace;

        using (var caller = new Activity("caller").Start())
        {
            callerTrace = caller.TraceId;
            await AddAsync(destination: "email-module");
        }

        var id = await OutboxIdAsync();
        await DispatchAsync();

        var span = Span("send", id);
        Assert.AreEqual($"send {Type}", span.DisplayName);
        Assert.AreEqual(ActivityKind.Client, span.Kind);
        Assert.AreEqual(callerTrace, span.TraceId);
        Assert.AreEqual(ActivityStatusCode.Unset, span.Status);

        Assert.AreEqual("messagingkit", span.GetTagItem("messaging.system"));
        Assert.AreEqual("send", span.GetTagItem("messaging.operation.name"));
        Assert.AreEqual("send", span.GetTagItem("messaging.operation.type"));
        Assert.AreEqual("email-module", span.GetTagItem("messaging.destination.name"));
        Assert.AreEqual(Type, span.GetTagItem("messagingkit.message.type"));
        Assert.AreEqual(1, span.GetTagItem("messagingkit.message.attempt"));
        Assert.AreEqual("sent", span.GetTagItem("messagingkit.outcome"));
        Assert.IsNull(span.GetTagItem("error.type"));
        AssertNoRetiredNames(span);
    }

    [TestMethod]
    public async Task A_failed_send_carries_error_type_and_an_error_status()
    {
        TestHost.Transport.Behaviour = _ => Error.Transient("broker.unreachable", "broker unreachable");
        await AddAsync();
        var id = await OutboxIdAsync();

        await DispatchAsync();

        var span = Span("send", id);
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("broker unreachable", span.StatusDescription);
        Assert.AreEqual("broker.unreachable", span.GetTagItem("error.type"));
        Assert.AreEqual("retried", span.GetTagItem("messagingkit.outcome"));
        Assert.IsNull(span.GetTagItem("messaging.destination.name"), "no destination was given");
    }

    [TestMethod]
    public async Task A_handling_is_a_consumer_span_parented_to_the_staging_trace()
    {
        var headers = new Dictionary<string, string>();
        using var caller = new Activity("caller").Start();
        MessagingDiagnostics.InjectTraceContext(headers);
        caller.Stop();
        var envelope = Envelope() with { Headers = headers };

        await StoreAsync(envelope);
        await ProcessAsync();

        var span = Span("process", envelope.Id);
        Assert.AreEqual($"process {Type}", span.DisplayName);
        Assert.AreEqual(ActivityKind.Consumer, span.Kind);
        Assert.AreEqual(caller.TraceId, span.TraceId);
        Assert.AreEqual(caller.SpanId, span.ParentSpanId);
        Assert.AreEqual(ActivityStatusCode.Unset, span.Status);

        Assert.AreEqual("messagingkit", span.GetTagItem("messaging.system"));
        Assert.AreEqual("process", span.GetTagItem("messaging.operation.name"));
        Assert.AreEqual("process", span.GetTagItem("messaging.operation.type"));
        Assert.AreEqual(Type, span.GetTagItem("messagingkit.message.type"));
        Assert.AreEqual(1, span.GetTagItem("messagingkit.message.attempt"));
        Assert.AreEqual("processed", span.GetTagItem("messagingkit.outcome"));
        Assert.IsNull(span.GetTagItem("error.type"));
        AssertNoRetiredNames(span);
    }

    [TestMethod]
    public async Task A_failing_handler_records_the_exception_on_its_span()
    {
        TestHost.Handlers.Behaviour = _ => throw new InvalidOperationException("downstream down");
        var envelope = Envelope();
        await StoreAsync(envelope);

        await ProcessAsync();

        var span = Span("process", envelope.Id);
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("downstream down", span.StatusDescription);
        Assert.AreEqual("System.InvalidOperationException", span.GetTagItem("error.type"));
        Assert.AreEqual("retried", span.GetTagItem("messagingkit.outcome"));
        Assert.IsTrue(span.Events.Any(e => e.Name == "exception"), "the handler's exception should be recorded");
    }

    [TestMethod]
    public async Task An_undispatchable_row_still_gets_a_failed_span()
    {
        var envelope = Envelope() with { Type = "not-registered" };
        await StoreAsync(envelope);

        await ProcessAsync();

        var span = Span("process", envelope.Id);
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("unknown_message_type", span.GetTagItem("error.type"));
        Assert.AreEqual("dead", span.GetTagItem("messagingkit.outcome"));
    }

    private Activity Span(string operation, Guid messageId) =>
        _stopped.Single(a =>
            Equals(a.GetTagItem("messaging.operation.name"), operation)
            && Equals(a.GetTagItem("messaging.message.id"), messageId.ToString()));

    private static void AssertNoRetiredNames(Activity span)
    {
        foreach (var name in RetiredNames)
        {
            Assert.IsNull(span.GetTagItem(name), $"{name} is not a current semantic-convention attribute");
        }
    }

    private static Task AddAsync(string? destination = null) =>
        WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            services.GetRequiredService<IOutbox>().Add(new SendEmail("ada@example.com", "Hello"), destination);
            await db.SaveChangesAsync();
        });

    private static Task<Guid> OutboxIdAsync() =>
        WithDbAsync(db => db.Set<OutboxMessage>().Select(m => m.Id).SingleAsync());

    private static MessageEnvelope Envelope() => new()
    {
        Id = Guid.CreateVersion7(),
        Type = Type,
        Payload = """{"to":"ada@example.com","subject":"Hello"}""",
        CreatedAt = TestHost.Clock.GetUtcNow(),
    };

    private static Task<bool> StoreAsync(MessageEnvelope envelope) =>
        WithScopeAsync(services => services.GetRequiredService<IInbox>().TryStoreAsync(envelope));

    private static Task<int> DispatchAsync() =>
        TestHost.Services.GetRequiredService<OutboxDispatcher>().RunOnceAsync(CancellationToken.None);

    private static Task<int> ProcessAsync() =>
        TestHost.Services.GetRequiredService<InboxProcessor>().RunOnceAsync(CancellationToken.None);
}
