using EmailService.Features.Emails.Abstractions;
using EmailService.Features.Emails.Domain;
using EmailService.IntegrationTests.Infrastructure;
using EmailService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EmailService.IntegrationTests.Features.Emails;

[TestClass]
public class EmailQueueTests : ApiTest
{
    private static readonly TimeSpan Lock = TimeSpan.FromMinutes(2);

    [TestMethod]
    public async Task Claims_a_queued_message_and_marks_it_sending()
    {
        var enqueued = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));

        var claimed = await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock));

        Assert.HasCount(1, claimed);
        Assert.AreEqual(enqueued.Id, claimed[0].Id);
        Assert.AreEqual(EmailStatus.Sending, claimed[0].Status);
        Assert.AreEqual(1, claimed[0].AttemptCount);
        Assert.AreEqual(Factory.Clock.GetUtcNow().Add(Lock), claimed[0].LockedUntil);
    }

    [TestMethod]
    public async Task Skips_a_message_scheduled_for_the_future()
    {
        var future = Factory.Clock.GetUtcNow().AddHours(1);
        await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage(scheduledAt: future)));

        Assert.IsEmpty(await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)));

        Factory.Clock.Advance(TimeSpan.FromHours(2));

        Assert.HasCount(1, await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)));
    }

    [TestMethod]
    public async Task Two_concurrent_claimers_never_take_the_same_row()
    {
        for (var i = 0; i < 10; i++)
        {
            await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage($"user{i}@example.com")));
        }

        var first = WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock));
        var second = WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock));
        var batches = await Task.WhenAll(first, second);

        var ids = batches.SelectMany(batch => batch).Select(message => message.Id).ToList();

        Assert.HasCount(10, ids);
        Assert.HasCount(10, ids.Distinct());
    }

    [TestMethod]
    public async Task Honours_the_batch_size()
    {
        for (var i = 0; i < 5; i++)
        {
            await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage($"user{i}@example.com")));
        }

        var claimed = await WithQueueAsync(queue => queue.ClaimBatchAsync(2, Lock));

        Assert.HasCount(2, claimed);
    }

    [TestMethod]
    public async Task Reclaims_a_message_whose_lock_expired()
    {
        await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));
        await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock));

        Assert.IsEmpty(await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)));

        Factory.Clock.Advance(Lock + TimeSpan.FromSeconds(1));
        var reclaimed = await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock));

        Assert.HasCount(1, reclaimed);
        Assert.AreEqual(2, reclaimed[0].AttemptCount);
    }

    [TestMethod]
    public async Task Marks_a_message_sent()
    {
        var message = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));
        var claimed = (await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)))[0];

        await WithQueueAsync(queue => queue.MarkSentAsync(claimed, "provider-id-1"));

        var stored = await WithQueueAsync(queue => queue.FindAsync(message.Id));
        Assert.AreEqual(EmailStatus.Sent, stored!.Status);
        Assert.AreEqual("provider-id-1", stored.ProviderMessageId);
        Assert.AreEqual(Factory.Clock.GetUtcNow(), stored.SentAt);
        Assert.IsNull(stored.LockedUntil);
    }

    [TestMethod]
    public async Task Requeues_a_transient_failure_with_a_delay()
    {
        var message = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));
        var claimed = (await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)))[0];

        await WithQueueAsync(queue => queue.MarkFailedAsync(claimed, "connection refused", false, TimeSpan.FromMinutes(5)));

        var stored = await WithQueueAsync(queue => queue.FindAsync(message.Id));
        Assert.AreEqual(EmailStatus.Queued, stored!.Status);
        Assert.AreEqual("connection refused", stored.LastError);
        Assert.AreEqual(Factory.Clock.GetUtcNow().AddMinutes(5), stored.ScheduledAt);
        Assert.IsEmpty(await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)));
    }

    [TestMethod]
    public async Task Kills_a_permanent_failure_immediately()
    {
        var message = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));
        var claimed = (await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)))[0];

        await WithQueueAsync(queue => queue.MarkFailedAsync(claimed, "550 no such mailbox", true, TimeSpan.FromMinutes(5)));

        var stored = await WithQueueAsync(queue => queue.FindAsync(message.Id));
        Assert.AreEqual(EmailStatus.Dead, stored!.Status);
    }

    [TestMethod]
    public async Task Kills_a_message_once_attempts_are_exhausted()
    {
        await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage(maxAttempts: 1)));
        var claimed = (await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock)))[0];

        await WithQueueAsync(queue => queue.MarkFailedAsync(claimed, "timeout", false, TimeSpan.FromSeconds(30)));

        var stored = await WithQueueAsync(queue => queue.FindAsync(claimed.Id));
        Assert.AreEqual(EmailStatus.Dead, stored!.Status);
    }

    [TestMethod]
    public async Task Rejects_a_duplicate_idempotency_key_at_the_database()
    {
        await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage(idempotencyKey: "dupe-1")));

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
            WithQueueAsync(queue => queue.EnqueueAsync(NewMessage(idempotencyKey: "dupe-1"))));
    }

    [TestMethod]
    public async Task Round_trips_attachments_and_headers_through_jsonb()
    {
        var message = NewMessage();
        message.Attachments.Add(new EmailAttachment
        {
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
            Content = Convert.ToBase64String("hello"u8.ToArray()),
            ContentId = "invoice",
        });
        message.Headers["X-Campaign"] = "welcome";
        message.Cc.Add("cc@example.com");

        var saved = await WithQueueAsync(queue => queue.EnqueueAsync(message));
        var stored = await WithQueueAsync(queue => queue.FindAsync(saved.Id));

        Assert.HasCount(1, stored!.Attachments);
        Assert.AreEqual("invoice.pdf", stored.Attachments[0].FileName);
        Assert.AreEqual("invoice", stored.Attachments[0].ContentId);
        Assert.AreEqual("welcome", stored.Headers["X-Campaign"]);
        Assert.AreEqual("cc@example.com", stored.Cc.Single());
    }

    [TestMethod]
    public async Task Cancels_a_queued_message_but_not_a_claimed_one()
    {
        var queued = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));
        Assert.IsTrue(await WithQueueAsync(queue => queue.CancelAsync(queued.Id)));

        var claimable = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage("other@example.com")));
        await WithQueueAsync(queue => queue.ClaimBatchAsync(10, Lock));

        Assert.IsFalse(await WithQueueAsync(queue => queue.CancelAsync(claimable.Id)));
    }

    [TestMethod]
    public async Task Filters_the_listing_by_status_and_recipient()
    {
        await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage("keep@example.com")));
        await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage("other@example.com")));

        var byRecipient = await WithQueueAsync(queue =>
            queue.ListAsync(new EmailQueryFilter(Recipient: "keep@example.com")));

        var byStatus = await WithQueueAsync(queue =>
            queue.ListAsync(new EmailQueryFilter(Status: EmailStatus.Sent)));

        Assert.HasCount(1, byRecipient);
        Assert.IsEmpty(byStatus);
    }

    [TestMethod]
    public async Task Applies_every_migration_and_leaves_none_pending()
    {
        var pending = await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<EmailDbContext>();
            return (await db.Database.GetPendingMigrationsAsync()).ToList();
        });

        Assert.IsEmpty(pending);
    }
}
