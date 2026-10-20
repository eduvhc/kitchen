using MailingKit.Domain;
using MailingKit.Options;
using MailingKit.Templates;
using MailingKit.Templating;
using MailingKit.Transport;
using MessagingKit.Abstractions;
using Microsoft.EntityFrameworkCore;
using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.Extensions.Time.Testing;
using ResultingKit;

namespace MailingKit.UnitTests;

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
        _sender.Behaviour = _ => Error.Transient("smtp.unavailable", "connection refused");

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
        _sender.Behaviour = _ => Error.Failure("smtp.rejected", "550 mailbox unavailable");

        var error = await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Hello",
            Text = "Hello",
        }));

        Assert.IsTrue(error.IsPermanent, "a 5xx reply cannot be fixed by retrying");
    }

    [TestMethod]
    public async Task Treats_every_non_transient_error_as_permanent()
    {
        await using var db = NewContext();
        _sender.Behaviour = _ => Error.Validation("smtp.malformed_address", "Malformed address: ada@");

        var error = await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Hello",
            Text = "Hello",
        }));

        Assert.IsTrue(error.IsPermanent);
        StringAssert.StartsWith(error.Message, "smtp.malformed_address: ");

        var log = await db.Set<EmailLog>().SingleAsync();
        Assert.AreEqual("smtp.malformed_address: Malformed address: ada@", log.LastError);
    }

    [TestMethod]
    public async Task Records_a_send_without_a_provider_id()
    {
        await using var db = NewContext();
        _sender.Behaviour = _ => new SendReceipt();

        await HandleAsync(db, new SendEmail
        {
            To = ["ada@example.com"],
            Subject = "Hello",
            Text = "Hello",
        });

        var log = await db.Set<EmailLog>().SingleAsync();
        Assert.AreEqual(EmailStatus.Sent, log.Status);
        Assert.IsNull(log.ProviderMessageId);
        Assert.IsNull(log.LastError);
    }

    [TestMethod]
    public async Task A_retry_updates_its_row_rather_than_adding_another()
    {
        await using var db = NewContext();
        var messageId = Guid.CreateVersion7();
        FailFirstAttempt();

        var message = new SendEmail { To = ["ada@example.com"], Subject = "Hello", Text = "Hello" };

        await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, message, messageId, attempt: 1));
        await HandleAsync(db, message, messageId, attempt: 2);

        var log = await db.Set<EmailLog>().SingleAsync();
        Assert.AreEqual(2, log.AttemptCount);
        Assert.AreEqual(EmailStatus.Sent, log.Status);
    }

    [TestMethod]
    public async Task A_message_already_sent_is_not_sent_again()
    {
        await using var db = NewContext();
        var messageId = Guid.CreateVersion7();
        var message = new SendEmail { To = ["ada@example.com"], Subject = "Hello", Text = "Hello" };

        await HandleAsync(db, message, messageId, attempt: 1);
        // The inbox lost track of the success and ran the handler again.
        await HandleAsync(db, message, messageId, attempt: 2);

        Assert.HasCount(1, _sender.Attempts);
        Assert.AreEqual(1, (await db.Set<EmailLog>().SingleAsync()).AttemptCount);
    }

    [TestMethod]
    public async Task A_retry_sends_what_the_first_attempt_rendered_even_after_the_template_changed()
    {
        await using var db = NewContext();
        var messageId = Guid.CreateVersion7();
        _templates.Templates["welcome"] = Welcome("Welcome {{ name }}", "<p>Hi {{ name }}</p>");
        FailFirstAttempt();

        var message = new SendEmail
        {
            To = ["ada@example.com"],
            Template = "welcome",
            Model = new Dictionary<string, object?> { ["name"] = "Ada" },
        };

        await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, message, messageId, attempt: 1));

        _templates.Templates["welcome"] = Welcome("Hello again {{ name }}", "<p>New copy for {{ name }}</p>");

        await HandleAsync(db, message, messageId, attempt: 2);

        var (first, retry) = (_sender.Attempts[0], _sender.Attempts[1]);
        Assert.AreEqual("Welcome Ada", retry.Subject);
        Assert.AreEqual(first.Subject, retry.Subject);
        Assert.AreEqual(first.HtmlBody, retry.HtmlBody);
        Assert.AreEqual(first.TextBody, retry.TextBody);
        Assert.AreEqual(first.FromAddress, retry.FromAddress);
        Assert.AreEqual(first.IdempotencyKey, retry.IdempotencyKey);
    }

    [TestMethod]
    public async Task A_retry_sends_even_after_the_template_was_deactivated()
    {
        await using var db = NewContext();
        var messageId = Guid.CreateVersion7();
        _templates.Templates["welcome"] = Welcome("Welcome", "<p>Hi</p>");
        FailFirstAttempt();

        var message = new SendEmail { To = ["ada@example.com"], Template = "welcome" };

        await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, message, messageId, attempt: 1));

        _templates.Templates["welcome"].IsActive = false;

        await HandleAsync(db, message, messageId, attempt: 2);

        Assert.AreEqual("<p>Hi</p>", _sender.Sent.Single().HtmlBody);
    }

    [TestMethod]
    public async Task Content_is_pinned_before_the_first_send()
    {
        await using var db = NewContext();
        EmailLog? duringSend = null;
        _sender.Behaviour = _ =>
        {
            // What a retry would find if the process died while the provider was answering.
            duringSend = db.Set<EmailLog>().AsNoTracking().Single();
            return new SendReceipt("provider-1");
        };

        await HandleAsync(db, new SendEmail { To = ["ada@example.com"], Subject = "Hello", Html = "<p>Hello</p>" });

        Assert.IsNotNull(duringSend);
        Assert.AreEqual("<p>Hello</p>", duringSend.HtmlBody);
        Assert.AreEqual(EmailStatus.Failed, duringSend.Status);
        Assert.AreEqual(SendEmailHandler<TestDbContext>.InterruptedError, duringSend.LastError);
    }

    [TestMethod]
    public async Task Bodies_are_kept_while_failing_and_cleared_once_sent()
    {
        await using var db = NewContext();
        var messageId = Guid.CreateVersion7();
        FailFirstAttempt();

        var message = new SendEmail { To = ["ada@example.com"], Subject = "Hello", Html = "<p>Hello</p>", Text = "Hello" };

        await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, message, messageId, attempt: 1));

        var failed = await db.Set<EmailLog>().AsNoTracking().SingleAsync();
        Assert.AreEqual("<p>Hello</p>", failed.HtmlBody);
        Assert.AreEqual("Hello", failed.TextBody);

        await HandleAsync(db, message, messageId, attempt: 2);

        var sent = await db.Set<EmailLog>().AsNoTracking().SingleAsync();
        Assert.IsNull(sent.HtmlBody);
        Assert.IsNull(sent.TextBody);
    }

    [TestMethod]
    public async Task A_retry_reuses_the_idempotency_key()
    {
        await using var db = NewContext();
        var messageId = Guid.CreateVersion7();
        var keys = new List<string?>();
        _sender.Behaviour = email =>
        {
            keys.Add(email.IdempotencyKey);
            // The provider accepted the first attempt, but its answer never arrived.
            return keys.Count == 1 ? Error.Transient("resend.timeout", "no response") : new SendReceipt("provider-1");
        };

        var message = new SendEmail { To = ["ada@example.com"], Subject = "Hello", Text = "Hello" };

        await Assert.ThrowsExactlyAsync<EmailSendException>(() => HandleAsync(db, message, messageId, attempt: 1));
        await HandleAsync(db, message, messageId, attempt: 2);

        Assert.HasCount(2, keys);
        Assert.IsFalse(string.IsNullOrWhiteSpace(keys[0]));
        Assert.AreEqual(keys[0], keys[1]);
    }

    [TestMethod]
    public async Task Different_messages_get_different_idempotency_keys()
    {
        await using var db = NewContext();
        var message = new SendEmail { To = ["ada@example.com"], Subject = "Hello", Text = "Hello" };

        await HandleAsync(db, message);
        await HandleAsync(db, message);

        Assert.HasCount(2, _sender.Sent);
        Assert.AreNotEqual(_sender.Sent[0].IdempotencyKey, _sender.Sent[1].IdempotencyKey);
    }

    [TestMethod]
    public void The_idempotency_key_fits_every_provider_limit()
    {
        var key = SendEmailHandler<TestDbContext>.IdempotencyKeyFor(Guid.CreateVersion7());

        Assert.IsLessThanOrEqualTo(64, key.Length);
        StringAssert.Matches(key, new System.Text.RegularExpressions.Regex("^mailingkit-[0-9a-f]{32}$"));
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

    /// <summary>The provider accepts the first attempt, but its answer never arrives.</summary>
    private void FailFirstAttempt() =>
        _sender.Behaviour = _ => _sender.Attempts.Count == 1
            ? Error.Transient("resend.timeout", "no response")
            : new SendReceipt("provider-1");

    private static EmailTemplate Welcome(string subject, string html) => new()
    {
        Key = "welcome",
        SubjectTemplate = subject,
        HtmlTemplate = html,
    };

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
