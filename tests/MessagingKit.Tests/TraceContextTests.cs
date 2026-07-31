using System.Diagnostics;
using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using MessagingKit.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.Tests;

[TestClass]
public class TraceContextTests : MessagingTest
{
    [TestMethod]
    public async Task Captures_the_ambient_trace_context_when_the_message_is_staged()
    {
        using var activity = new Activity("caller").Start();

        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Hello"));

        var stored = await StoredAsync();

        Assert.IsTrue(
            stored.Headers.ContainsKey(MessagingDiagnostics.TraceParentHeader),
            "the staged message should carry the caller's trace context");

        StringAssert.Contains(
            stored.Headers[MessagingDiagnostics.TraceParentHeader],
            activity.TraceId.ToString());
    }

    [TestMethod]
    public async Task Leaves_headers_alone_when_nothing_is_tracing()
    {
        // Guards against writing a malformed traceparent when there is no ambient activity.
        Assert.IsNull(Activity.Current, "another test leaked an activity");

        await Outbox.OutboxTests.AddAsync(new SendEmail("ada@example.com", "Hello"));

        var stored = await StoredAsync();

        Assert.IsFalse(stored.Headers.ContainsKey(MessagingDiagnostics.TraceParentHeader));
    }

    [TestMethod]
    public async Task Preserves_caller_supplied_headers_alongside_the_trace_context()
    {
        using var activity = new Activity("caller").Start();

        await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            services.GetRequiredService<IOutbox>().Add(
                new SendEmail("ada@example.com", "Hello"),
                headers: new Dictionary<string, string> { ["tenant"] = "acme" });
            await db.SaveChangesAsync();
        });

        var stored = await StoredAsync();

        Assert.AreEqual("acme", stored.Headers["tenant"]);
        Assert.IsTrue(stored.Headers.ContainsKey(MessagingDiagnostics.TraceParentHeader));
    }

    private static Task<OutboxMessage> StoredAsync() =>
        WithDbAsync(db => db.Set<OutboxMessage>().AsNoTracking().SingleAsync());
}
