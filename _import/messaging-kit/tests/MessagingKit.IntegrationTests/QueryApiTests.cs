using MessagingKit.Inbox.Abstractions;
using MessagingKit.Inbox.Domain;
using MessagingKit.Outbox.Abstractions;
using MessagingKit.Testing;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.IntegrationTests;

/// <summary>
/// The read side callers reach for when asking "why didn't that message go out?".
/// </summary>
[TestClass]
public class QueryApiTests : MessagingTest
{
    [TestMethod]
    public async Task Finds_an_outbox_message_by_id()
    {
        var id = await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            var row = services.GetRequiredService<IOutbox>().Add(new SendEmail("ada@example.com", "Hello"));
            await db.SaveChangesAsync();
            return row.Id;
        });

        var found = await WithScopeAsync(services =>
            services.GetRequiredService<IOutboxStore>().FindAsync(id));

        Assert.IsNotNull(found);
        Assert.AreEqual(id, found.Id);
        Assert.AreEqual("send-email", found.Type);
    }

    [TestMethod]
    public async Task Returns_null_for_an_unknown_outbox_id()
    {
        var found = await WithScopeAsync(services =>
            services.GetRequiredService<IOutboxStore>().FindAsync(Guid.CreateVersion7()));

        Assert.IsNull(found);
    }

    [TestMethod]
    public async Task Finds_an_inbox_message_by_id()
    {
        var envelope = Envelope();

        await WithScopeAsync(services => services.GetRequiredService<IInbox>().TryStoreAsync(envelope));

        var found = await WithScopeAsync(services =>
            services.GetRequiredService<IInbox>().FindAsync(envelope.Id));

        Assert.IsNotNull(found);
        Assert.AreEqual(InboxStatus.Pending, found.Status);
        Assert.AreEqual("send-email", found.Type);
    }

    [TestMethod]
    public async Task Returns_null_for_an_unknown_inbox_id()
    {
        var found = await WithScopeAsync(services =>
            services.GetRequiredService<IInbox>().FindAsync(Guid.CreateVersion7()));

        Assert.IsNull(found);
    }

    [TestMethod]
    public async Task Drains_only_the_inbox_when_asked()
    {
        await WithScopeAsync(services => services.GetRequiredService<IInbox>().TryStoreAsync(Envelope()));

        var processed = await TestHost.Services.DrainInboxAsync();

        Assert.AreEqual(1, processed);
        Assert.HasCount(1, TestHost.Handlers.Handled);
    }

    private static MessageEnvelope Envelope() => new()
    {
        Id = Guid.CreateVersion7(),
        Type = "send-email",
        Payload = """{"to":"ada@example.com","subject":"Hello"}""",
        CreatedAt = TestHost.Clock.GetUtcNow(),
    };
}
