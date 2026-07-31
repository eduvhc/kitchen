using MailingKit.Domain;
using MailingKit.Options;
using MailingKit.Templates;
using MailingKit.Templating;
using MailingKit.Transport;
using MessagingKit;
using Microsoft.EntityFrameworkCore;
using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.Extensions.Time.Testing;

namespace MailingKit.Tests;

[TestClass]
public class SendEmailHandlerTests
{
    private readonly FakeEmailSender _sender = new();
    private readonly FakeTemplateStore _templates = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    [TestMethod]
    public async Task Sends_an_email_and_records_it()
    {
        await using var db = NewContext();

        await HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Hello",
            Html = "<p>Hello</p>",
        });

        var sent = _sender.Sent.Single();
        Assert.AreEqual("ada@example.com", sent.To.Single());
        Assert.AreEqual("Hello", sent.Subject);

        var log = await db.Set<EmailLog>().SingleAsync();
        Assert.AreEqual(EmailStatus.Sent, log.Status);
        Assert.AreEqual("provider-1", log.ProviderMessageId);
        Assert.AreEqual(_clock.GetUtcNow(), log.SentAt);
    }

    [TestMethod]
    public async Task Renders_the_named_template()
    {
        await using var db = NewContext();

        _templates.Templates["welcome"] = new EmailTemplate
        {
            Key = "welcome",
            SubjectTemplate = "Welcome {{ name }}",
            HtmlTemplate = "<p>Hi {{ name }}</p>",
        };

        await HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Template = "welcome",
            Model = new Dictionary<string, object?> { ["name"] = "Ada" },
        });

        var sent = _sender.Sent.Single();
        Assert.AreEqual("Welcome Ada", sent.Subject);
        StringAssert.Contains(sent.HtmlBody, "Hi Ada");
    }

    [TestMethod]
    public async Task Throws_when_the_template_is_missing()
    {
        await using var db = NewContext();

        await Assert.ThrowsExactlyAsync<ValidationException>(() => HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Template = "nope",
        }));

        Assert.IsEmpty(_sender.Sent);
    }

    [TestMethod]
    public async Task Records_a_failure_and_throws_so_the_inbox_retries()
    {
        await using var db = NewContext();
        _sender.Behaviour = _ => SendResult.Transient("connection refused");

        var error = await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Hello",
            Text = "Hello",
        }));

        Assert.IsFalse(error.IsPermanent);

        var log = await db.Set<EmailLog>().SingleAsync();
        Assert.AreEqual(EmailStatus.Failed, log.Status);
        StringAssert.Contains(log.LastError, "connection refused");
        Assert.IsNull(log.SentAt);
    }

    [TestMethod]
    public async Task Marks_a_permanent_failure_as_such()
    {
        await using var db = NewContext();
        _sender.Behaviour = _ => SendResult.Permanent("550 mailbox unavailable");

        var error = await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Hello",
            Text = "Hello",
        }));

        Assert.IsTrue(error.IsPermanent, "a 5xx reply cannot be fixed by retrying");
    }

    [TestMethod]
    public async Task A_redelivered_message_updates_its_row_rather_than_adding_another()
    {
        await using var db = NewContext();
        var messageId = Guid.CreateVersion7();

        var message = new SendEmail { To = ["ada@example.com"], Subject = "Hello", Text = "Hello" };

        await HandleAsync(db, message, messageId, attempt: 1);
        await HandleAsync(db, message, messageId, attempt: 2);

        var log = await db.Set<EmailLog>().SingleAsync();
        Assert.AreEqual(2, log.AttemptCount);
        Assert.HasCount(2, _sender.Sent, "the handler does not deduplicate; the inbox does");
    }

    [TestMethod]
    public async Task Rejects_a_message_with_no_recipients()
    {
        await using var db = NewContext();

        await Assert.ThrowsExactlyAsync<ValidationException>(() =>
            HandleAsync(db, new SendEmail { Subject = "Hello", Text = "Hello" }));
    }

    [TestMethod]
    public async Task Rejects_a_message_with_no_body()
    {
        await using var db = NewContext();

        await Assert.ThrowsExactlyAsync<ValidationException>(() =>
            HandleAsync(db, new SendEmail { To = ["ada@example.com"], Subject = "Hello" }));
    }

    private Task HandleAsync(TestDbContext db, SendEmail message, Guid? messageId = null, int attempt = 1)
    {
        var handler = new SendEmailHandler<TestDbContext>(
            db,
            _sender,
            new ScribanTemplateRenderer(),
            _clock,
            MsOptions.Create(new MailingOptions()),
            _templates);

        var context = new MessageContext
        {
            MessageId = messageId ?? Guid.CreateVersion7(),
            Type = "send-email",
            AttemptCount = attempt,
        };

        return handler.HandleAsync(message, context);
    }

    private static TestDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.CreateVersion7().ToString())
            .Options);
}
