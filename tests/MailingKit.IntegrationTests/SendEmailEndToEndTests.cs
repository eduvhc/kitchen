using MailingKit.Domain;
using MailingKit.Templates;
using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Domain;
using MessagingKit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestingKit;
using TestingKit.MSTest;

namespace MailingKit.IntegrationTests;

/// <summary>
/// The whole path: a module stages a message in its own transaction, MessagingKit carries it to the
/// inbox, MailingKit handles it, and the mail lands in a real SMTP server.
/// </summary>
[TestClass]
public class SendEmailEndToEndTests : IntegrationTest
{
    protected override TestEnvironment Environment => TestHost.Environment;

    [TestMethod]
    public async Task Carries_a_staged_message_all_the_way_to_the_mailbox()
    {
        await StageAsync(new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Your invoice",
            Html = "<p>Attached</p>",
            Source = "billing",
        });

        await TestHost.Services.DrainMessagingAsync(ct: TestContext.CancellationTokenSource.Token);

        var received = await TestHost.Smtp.WaitForMessageAsync(
            m => m.To.Any(a => a.Address == "ada@example.com"),
            TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("Your invoice", received.Subject);

        var log = await WithDbAsync(db => db.Set<EmailLog>().AsNoTracking().SingleAsync());
        Assert.AreEqual(EmailStatus.Sent, log.Status);
        Assert.AreEqual("billing", log.Source);
        Assert.AreEqual("ada@example.com", log.To.Single());
        Assert.IsNotNull(log.SentAt);
    }

    [TestMethod]
    public async Task Renders_a_template_stored_in_the_database()
    {
        await WithDbAsync(async db =>
        {
            db.Set<EmailTemplate>().Add(new EmailTemplate
            {
                Key = "welcome",
                SubjectTemplate = "Welcome {{ name }}",
                HtmlTemplate = "<p>Hi {{ name }}</p>",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        await StageAsync(new SendEmail
        {
            To = ["grace@example.com"],
            Template = "welcome",
            Model = new Dictionary<string, object?> { ["name"] = "Grace" },
        });

        await TestHost.Services.DrainMessagingAsync(ct: TestContext.CancellationTokenSource.Token);

        var received = await TestHost.Smtp.WaitForMessageAsync(
            m => m.To.Any(a => a.Address == "grace@example.com"),
            TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("Welcome Grace", received.Subject);
    }

    [TestMethod]
    public async Task Nothing_is_sent_when_the_sending_transaction_rolls_back()
    {
        await using (var scope = TestHost.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            scope.ServiceProvider.GetRequiredService<IOutbox>()
                .Add(new SendEmail { To = ["rolled-back@example.com"], Subject = "No", Text = "No" });

            // Never saved: the outbox row only exists if the caller's transaction commits.
        }

        await TestHost.Services.DrainMessagingAsync(ct: TestContext.CancellationTokenSource.Token);

        var staged = await WithDbAsync(db => db.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, staged);

        var logged = await WithDbAsync(db => db.Set<EmailLog>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, logged);
    }

    [TestMethod]
    public async Task Marks_the_outbox_row_sent_once_it_reaches_the_inbox()
    {
        await StageAsync(new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Hello",
            Text = "Hello",
        });

        await TestHost.Services.DrainOutboxAsync(TestContext.CancellationTokenSource.Token);

        var staged = await WithDbAsync(db => db.Set<OutboxMessage>().AsNoTracking().SingleAsync());
        Assert.AreEqual(OutboxStatus.Sent, staged.Status);

        // Delivered but not yet handled — the two halves really are separate.
        var logged = await WithDbAsync(db => db.Set<EmailLog>().AsNoTracking().CountAsync());
        Assert.AreEqual(0, logged);
    }

    [TestMethod]
    public async Task A_message_is_handled_once_even_when_it_is_delivered_twice()
    {
        await StageAsync(new SendEmail
        {
            To = ["once@example.com"],
            Subject = "Once",
            Text = "Once",
        });

        // Draining repeatedly re-runs both halves; the inbox key stops a second handle.
        await TestHost.Services.DrainMessagingAsync(ct: TestContext.CancellationTokenSource.Token);
        await TestHost.Services.DrainMessagingAsync(ct: TestContext.CancellationTokenSource.Token);

        var logs = await WithDbAsync(db => db.Set<EmailLog>().AsNoTracking().CountAsync());
        Assert.AreEqual(1, logs);
    }

    private static Task StageAsync(SendEmail message) =>
        WithDbScopeAsync(async (db, services) =>
        {
            services.GetRequiredService<IOutbox>().Add(message);
            await db.SaveChangesAsync();
        });

    private static async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = TestHost.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = TestHost.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task WithDbScopeAsync(Func<AppDbContext, IServiceProvider, Task> action)
    {
        await using var scope = TestHost.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider);
    }
}
