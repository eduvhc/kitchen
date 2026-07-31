using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit.IntegrationTests.Outbox;

[TestClass]
public class OutboxTests : MessagingTest
{
    [TestMethod]
    public async Task Commits_the_message_with_the_business_row()
    {
        await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            var outbox = services.GetRequiredService<IOutbox>();

            db.Invoices.Add(new Invoice { Reference = "INV-1" });
            outbox.Add(new SendEmail("ada@example.com", "Your invoice"));

            await db.SaveChangesAsync();
        });

        var invoices = await WithDbAsync(db => db.Invoices.CountAsync());
        var messages = await WithDbAsync(db => db.Set<OutboxMessage>().CountAsync());

        Assert.AreEqual(1, invoices);
        Assert.AreEqual(1, messages);
    }

    [TestMethod]
    public async Task Writes_nothing_when_the_transaction_rolls_back()
    {
        await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            var outbox = services.GetRequiredService<IOutbox>();

            await using var transaction = await db.Database.BeginTransactionAsync();

            db.Invoices.Add(new Invoice { Reference = "INV-2" });
            outbox.Add(new SendEmail("ada@example.com", "Rolled back"));
            await db.SaveChangesAsync();

            await transaction.RollbackAsync();
        });

        Assert.AreEqual(0, await WithDbAsync(db => db.Invoices.CountAsync()));
        Assert.AreEqual(0, await WithDbAsync(db => db.Set<OutboxMessage>().CountAsync()));
    }

    [TestMethod]
    public async Task Serializes_the_payload_and_records_the_registered_type_name()
    {
        await AddAsync(new SendEmail("ada@example.com", "Hello"));

        var stored = await WithDbAsync(db => db.Set<OutboxMessage>().AsNoTracking().SingleAsync());

        Assert.AreEqual("send-email", stored.Type);
        Assert.Contains("ada@example.com", stored.Payload);
        Assert.AreEqual(OutboxStatus.Pending, stored.Status);
    }

    [TestMethod]
    public async Task Records_destination_headers_and_schedule()
    {
        var sendAt = TestHost.Clock.GetUtcNow().AddHours(3);

        await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            services.GetRequiredService<IOutbox>().Add(
                new SendEmail("ada@example.com", "Later"),
                destination: "email",
                headers: new Dictionary<string, string> { ["tenant"] = "acme" },
                sendAt: sendAt);

            await db.SaveChangesAsync();
        });

        var stored = await WithDbAsync(db => db.Set<OutboxMessage>().AsNoTracking().SingleAsync());

        Assert.AreEqual("email", stored.Destination);
        Assert.AreEqual("acme", stored.Headers["tenant"]);
        Assert.AreEqual(sendAt, stored.ScheduledAt);
    }

    internal static Task AddAsync(SendEmail message) =>
        WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<TestDbContext>();
            services.GetRequiredService<IOutbox>().Add(message);
            await db.SaveChangesAsync();
        });
}
