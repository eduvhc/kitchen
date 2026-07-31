using MessagingKit.Outbox;
using MessagingKit.Outbox.Domain;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.IntegrationTests.Outbox;

[TestClass]
public class OutboxDispatcherTests : MessagingTest
{
    [TestMethod]
    public async Task Delivers_a_pending_message_and_marks_it_sent()
    {
        await OutboxTests.AddAsync(new SendEmail("ada@example.com", "Hello"));

        var processed = await RunAsync();

        Assert.AreEqual(1, processed);
        Assert.HasCount(1, TestHost.Transport.Sent);

        var envelope = TestHost.Transport.Sent.Single();
        Assert.AreEqual("send-email", envelope.Type);
        Assert.AreEqual(1, envelope.AttemptCount);

        var stored = await StoredAsync();
        Assert.AreEqual(OutboxStatus.Sent, stored.Status);
        Assert.AreEqual(TestHost.Clock.GetUtcNow(), stored.SentAt);
    }

    [TestMethod]
    public async Task Reschedules_a_transient_failure_with_backoff()
    {
        TestHost.Transport.Behaviour = _ => TransportResult.Transient("broker unreachable");
        await OutboxTests.AddAsync(new SendEmail("ada@example.com", "Retry me"));

        await RunAsync();

        var stored = await StoredAsync();
        Assert.AreEqual(OutboxStatus.Pending, stored.Status);
        Assert.AreEqual(1, stored.AttemptCount);
        Assert.AreEqual("broker unreachable", stored.LastError);
        Assert.IsGreaterThan(TestHost.Clock.GetUtcNow(), stored.ScheduledAt);

        Assert.AreEqual(0, await RunAsync());
    }

    [TestMethod]
    public async Task Kills_a_permanent_failure_without_retrying()
    {
        TestHost.Transport.Behaviour = _ => TransportResult.Permanent("unknown destination");
        await OutboxTests.AddAsync(new SendEmail("ada@example.com", "Doomed"));

        await RunAsync();

        var stored = await StoredAsync();
        Assert.AreEqual(OutboxStatus.Dead, stored.Status);
    }

    [TestMethod]
    public async Task Retries_until_max_attempts_then_marks_dead()
    {
        TestHost.Transport.Behaviour = _ => TransportResult.Transient("still down");
        await OutboxTests.AddAsync(new SendEmail("ada@example.com", "Persistent"));

        await WithDbAsync(async db =>
        {
            await db.Set<OutboxMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxAttempts, 2));
        });

        await RunAsync();
        TestHost.Clock.Advance(TimeSpan.FromHours(1));
        await RunAsync();

        var stored = await StoredAsync();
        Assert.AreEqual(OutboxStatus.Dead, stored.Status);
        Assert.AreEqual(2, stored.AttemptCount);
    }

    [TestMethod]
    public async Task Leaves_a_scheduled_message_until_its_time()
    {
        await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            services.GetRequiredService<MessagingKit.Outbox.Abstractions.IOutbox>()
                .Add(new SendEmail("ada@example.com", "Later"), sendAt: TestHost.Clock.GetUtcNow().AddHours(2));
            await db.SaveChangesAsync();
        });

        Assert.AreEqual(0, await RunAsync());

        TestHost.Clock.Advance(TimeSpan.FromHours(3));

        Assert.AreEqual(1, await RunAsync());
    }

    [TestMethod]
    public async Task Reclaims_a_message_whose_lock_expired()
    {
        await OutboxTests.AddAsync(new SendEmail("ada@example.com", "Stuck"));

        await WithDbAsync(async db =>
        {
            var now = TestHost.Clock.GetUtcNow();
            await db.Set<OutboxMessage>().ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Sending)
                .SetProperty(m => m.LockedUntil, now.AddMinutes(1)));
        });

        Assert.AreEqual(0, await RunAsync());

        TestHost.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.AreEqual(1, await RunAsync());
    }

    [TestMethod]
    public async Task Two_dispatchers_never_claim_the_same_message()
    {
        for (var i = 0; i < 10; i++)
        {
            await OutboxTests.AddAsync(new SendEmail($"user{i}@example.com", "Concurrent"));
        }

        var dispatcher = TestHost.Services.GetRequiredService<OutboxDispatcher>();
        var first = dispatcher.RunOnceAsync(CancellationToken.None);
        var second = dispatcher.RunOnceAsync(CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.AreEqual(10, first.Result + second.Result);
        Assert.HasCount(10, TestHost.Transport.Sent);
        Assert.HasCount(10, TestHost.Transport.Sent.Select(e => e.Id).Distinct());
    }

    [TestMethod]
    [DataRow(1, 10)]
    [DataRow(2, 20)]
    [DataRow(3, 40)]
    public void Backs_off_exponentially(int attempt, int expectedSeconds)
    {
        var dispatcher = TestHost.Services.GetRequiredService<OutboxDispatcher>();
        Assert.AreEqual(expectedSeconds, dispatcher.BackoffFor(attempt).TotalSeconds);
    }

    private static Task<int> RunAsync() =>
        TestHost.Services.GetRequiredService<OutboxDispatcher>().RunOnceAsync(CancellationToken.None);

    private static Task<OutboxMessage> StoredAsync() =>
        WithDbAsync(db => db.Set<OutboxMessage>().AsNoTracking().SingleAsync());
}
