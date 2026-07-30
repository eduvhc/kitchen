using EmailService.Common;
using EmailService.Features.Emails;
using EmailService.Features.Emails.SendEmail;
using EmailService.Features.Templates;
using EmailService.Options;
using EmailService.Templating;
using EmailService.Tests.TestDoubles;
using Microsoft.Extensions.Options;

namespace EmailService.Tests.Features.Emails.SendEmail;

[TestClass]
public class SendEmailHandlerTests
{
    private readonly FakeEmailQueue _queue = new();
    private readonly FakeTemplateStore _templates = new();

    private SendEmailHandler CreateHandler(EmailDefaultsOptions? defaults = null) =>
        new(
            _queue,
            _templates,
            new ScribanTemplateRenderer(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(
                defaults ?? new EmailDefaultsOptions { FromAddress = "no-reply@example.com" }));

    [TestMethod]
    public async Task Queues_a_raw_email()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new SendEmailRequest
            {
                To = ["someone@example.com"],
                Subject = "Hello",
                Text = "Body",
            },
            source: "tests");

        Assert.IsFalse(result.Deduplicated);
        Assert.AreEqual(EmailStatus.Queued, result.Email.Status);
        Assert.HasCount(1, _queue.Messages);
        Assert.AreEqual("no-reply@example.com", _queue.Messages[0].FromAddress);
        Assert.AreEqual("tests", _queue.Messages[0].Source);
    }

    [TestMethod]
    public async Task Renders_a_stored_template()
    {
        await _templates.UpsertAsync(new EmailTemplate
        {
            Key = "welcome",
            SubjectTemplate = "Welcome {{ name }}",
            HtmlTemplate = "<p>Hi {{ name }}</p>",
            FromAddress = "hello@example.com",
        });

        var handler = CreateHandler();

        await handler.HandleAsync(
            new SendEmailRequest
            {
                To = ["someone@example.com"],
                Template = "welcome",
                Model = new Dictionary<string, object?> { ["name"] = "Ada" },
            },
            source: "tests");

        var message = _queue.Messages.Single();
        Assert.AreEqual("Welcome Ada", message.Subject);
        Assert.AreEqual("<p>Hi Ada</p>", message.HtmlBody);
        Assert.AreEqual("hello@example.com", message.FromAddress);
        Assert.AreEqual("welcome", message.TemplateKey);
    }

    [TestMethod]
    public async Task Request_fields_win_over_template_fields()
    {
        await _templates.UpsertAsync(new EmailTemplate
        {
            Key = "welcome",
            SubjectTemplate = "Welcome {{ name }}",
            HtmlTemplate = "<p>Hi {{ name }}</p>",
            FromAddress = "hello@example.com",
        });

        var handler = CreateHandler();

        await handler.HandleAsync(
            new SendEmailRequest
            {
                To = ["someone@example.com"],
                Template = "welcome",
                Subject = "Override",
                From = "billing@example.com",
                Model = new Dictionary<string, object?> { ["name"] = "Ada" },
            },
            source: "tests");

        var message = _queue.Messages.Single();
        Assert.AreEqual("Override", message.Subject);
        Assert.AreEqual("billing@example.com", message.FromAddress);
        Assert.AreEqual("<p>Hi Ada</p>", message.HtmlBody);
    }

    [TestMethod]
    public async Task Returns_the_existing_message_for_a_repeated_idempotency_key()
    {
        var handler = CreateHandler();
        var request = new SendEmailRequest
        {
            To = ["someone@example.com"],
            Subject = "Hello",
            Text = "Body",
            IdempotencyKey = "order-42",
        };

        var first = await handler.HandleAsync(request, "tests");
        var second = await handler.HandleAsync(request, "tests");

        Assert.IsFalse(first.Deduplicated);
        Assert.IsTrue(second.Deduplicated);
        Assert.AreEqual(first.Email.Id, second.Email.Id);
        Assert.HasCount(1, _queue.Messages);
    }

    [TestMethod]
    public async Task Schedules_a_future_send()
    {
        var handler = CreateHandler();
        var sendAt = DateTimeOffset.UtcNow.AddHours(2);

        await handler.HandleAsync(
            new SendEmailRequest
            {
                To = ["someone@example.com"],
                Subject = "Hello",
                Text = "Body",
                SendAt = sendAt,
            },
            "tests");

        Assert.AreEqual(sendAt, _queue.Messages.Single().ScheduledAt);
    }

    [TestMethod]
    public async Task Rejects_a_message_without_recipients()
    {
        var handler = CreateHandler();

        await Assert.ThrowsExactlyAsync<ValidationException>(() => handler.HandleAsync(
            new SendEmailRequest { Subject = "Hello", Text = "Body" },
            "tests"));
    }

    [TestMethod]
    public async Task Rejects_a_message_without_a_body()
    {
        var handler = CreateHandler();

        await Assert.ThrowsExactlyAsync<ValidationException>(() => handler.HandleAsync(
            new SendEmailRequest { To = ["someone@example.com"], Subject = "Hello" },
            "tests"));
    }

    [TestMethod]
    public async Task Rejects_a_recipient_outside_the_allowlist()
    {
        var handler = CreateHandler(new EmailDefaultsOptions
        {
            FromAddress = "no-reply@example.com",
            AllowedRecipientDomains = ["example.com"],
        });

        await Assert.ThrowsExactlyAsync<ValidationException>(() => handler.HandleAsync(
            new SendEmailRequest { To = ["someone@other.com"], Subject = "Hello", Text = "Body" },
            "tests"));
    }

    [TestMethod]
    public async Task Rejects_more_recipients_than_the_limit()
    {
        var handler = CreateHandler(new EmailDefaultsOptions
        {
            FromAddress = "no-reply@example.com",
            MaxRecipients = 2,
        });

        await Assert.ThrowsExactlyAsync<ValidationException>(() => handler.HandleAsync(
            new SendEmailRequest
            {
                To = ["a@example.com", "b@example.com", "c@example.com"],
                Subject = "Hello",
                Text = "Body",
            },
            "tests"));
    }

    [TestMethod]
    public async Task Rejects_an_attachment_that_is_not_base64()
    {
        var handler = CreateHandler();

        await Assert.ThrowsExactlyAsync<ValidationException>(() => handler.HandleAsync(
            new SendEmailRequest
            {
                To = ["someone@example.com"],
                Subject = "Hello",
                Text = "Body",
                Attachments =
                [
                    new AttachmentDto { FileName = "a.pdf", ContentType = "application/pdf", Content = "not base64!" },
                ],
            },
            "tests"));
    }

    [TestMethod]
    public async Task Rejects_an_unknown_template()
    {
        var handler = CreateHandler();

        await Assert.ThrowsExactlyAsync<ValidationException>(() => handler.HandleAsync(
            new SendEmailRequest { To = ["someone@example.com"], Template = "missing" },
            "tests"));
    }

    [TestMethod]
    public async Task Rejects_an_inactive_template()
    {
        await _templates.UpsertAsync(new EmailTemplate
        {
            Key = "retired",
            SubjectTemplate = "Hello",
            TextTemplate = "Body",
            IsActive = false,
        });

        var handler = CreateHandler();

        await Assert.ThrowsExactlyAsync<ValidationException>(() => handler.HandleAsync(
            new SendEmailRequest { To = ["someone@example.com"], Template = "retired" },
            "tests"));
    }

    [TestMethod]
    public async Task Deduplicates_and_trims_recipients()
    {
        var handler = CreateHandler();

        await handler.HandleAsync(
            new SendEmailRequest
            {
                To = [" someone@example.com ", "SOMEONE@example.com"],
                Subject = "Hello",
                Text = "Body",
            },
            "tests");

        var message = _queue.Messages.Single();
        Assert.HasCount(1, message.To);
        Assert.AreEqual("someone@example.com", message.To[0]);
    }
}
