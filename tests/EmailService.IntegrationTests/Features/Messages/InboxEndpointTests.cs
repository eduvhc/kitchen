using System.Net;
using System.Net.Http.Json;
using EmailService.Features.Emails.Abstractions;
using EmailService.Features.Emails.Domain;
using EmailService.IntegrationTests.Infrastructure;
using MessagingKit;
using MessagingKit.Inbox;
using MessagingKit.Inbox.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace EmailService.IntegrationTests.Features.Messages;

[TestClass]
public class InboxEndpointTests : ApiTest
{
    [TestMethod]
    public async Task Accepts_a_message_and_reports_it_pending()
    {
        using var client = Factory.CreateApiClient();
        var envelope = Envelope();

        using var response = await client.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        var stored = await FindAsync(envelope.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(InboxStatus.Pending, stored.Status);
        Assert.AreEqual("send-email", stored.Type);
    }

    [TestMethod]
    public async Task Absorbs_a_redelivered_message_without_queueing_twice()
    {
        using var client = Factory.CreateApiClient();
        var envelope = Envelope();

        using var first = await client.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);
        using var second = await client.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);

        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        await RunInboxAsync();

        var queued = await WithQueueAsync(queue => queue.ListAsync(new EmailQueryFilter()));
        Assert.HasCount(1, queued);
    }

    [TestMethod]
    public async Task Processing_queues_the_email_with_the_message_id_as_idempotency_key()
    {
        using var client = Factory.CreateApiClient();
        var envelope = Envelope();
        using var accepted = await client.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);
        accepted.EnsureSuccessStatusCode();

        var processed = await RunInboxAsync();

        Assert.AreEqual(1, processed);

        var stored = await FindAsync(envelope.Id);
        Assert.AreEqual(InboxStatus.Processed, stored!.Status);

        var email = (await WithQueueAsync(queue => queue.ListAsync(new EmailQueryFilter()))).Single();
        Assert.AreEqual("ada@example.com", email.To.Single());
        Assert.AreEqual("Your invoice", email.Subject);
        Assert.AreEqual(EmailStatus.Queued, email.Status);
        Assert.AreEqual("inbox", email.Source);
        Assert.AreEqual(envelope.Id.ToString(), email.IdempotencyKey);
    }

    [TestMethod]
    public async Task Processing_twice_never_queues_a_second_email()
    {
        using var client = Factory.CreateApiClient();
        var envelope = Envelope();
        using var accepted = await client.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);
        accepted.EnsureSuccessStatusCode();

        await RunInboxAsync();
        var second = await RunInboxAsync();

        Assert.AreEqual(0, second);
        Assert.HasCount(1, await WithQueueAsync(queue => queue.ListAsync(new EmailQueryFilter())));
    }

    [TestMethod]
    public async Task Renders_a_template_when_the_message_names_one()
    {
        using var admin = Factory.CreateApiClient();
        using var upsert = await admin.PutAsJsonAsync(
            "/v1/templates/welcome",
            new { subject = "Welcome {{ name }}", html = "<p>Hi {{ name }}</p>" },
            CancellationToken);
        upsert.EnsureSuccessStatusCode();

        var envelope = Envelope("""{"to":["grace@example.com"],"template":"welcome","model":{"name":"Grace"}}""");
        using var accepted = await admin.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);
        accepted.EnsureSuccessStatusCode();

        await RunInboxAsync();

        var email = (await WithQueueAsync(queue => queue.ListAsync(new EmailQueryFilter()))).Single();
        Assert.AreEqual("Welcome Grace", email.Subject);
        Assert.AreEqual("<p>Hi Grace</p>", email.HtmlBody);
    }

    [TestMethod]
    public async Task Marks_an_invalid_payload_dead_without_queueing()
    {
        using var client = Factory.CreateApiClient();
        var envelope = Envelope("""{"to":[],"subject":"No recipients","text":"nope"}""");
        using var accepted = await client.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);
        accepted.EnsureSuccessStatusCode();

        await RunInboxAsync();

        var stored = await FindAsync(envelope.Id);
        Assert.AreEqual(InboxStatus.Pending, stored!.Status);
        Assert.Contains("recipient", stored.LastError!);
        Assert.IsEmpty(await WithQueueAsync(queue => queue.ListAsync(new EmailQueryFilter())));
    }

    [TestMethod]
    public async Task Marks_an_unregistered_type_dead()
    {
        using var client = Factory.CreateApiClient();
        var envelope = Envelope() with { Type = "send-carrier-pigeon" };
        using var accepted = await client.PostAsJsonAsync("/v1/messages", envelope, CancellationToken);
        accepted.EnsureSuccessStatusCode();

        await RunInboxAsync();

        var stored = await FindAsync(envelope.Id);
        Assert.AreEqual(InboxStatus.Dead, stored!.Status);
        Assert.Contains("send-carrier-pigeon", stored.LastError!);
    }

    [TestMethod]
    public async Task Rejects_an_envelope_without_an_id()
    {
        using var client = Factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/messages",
            new { id = Guid.Empty, type = "send-email", payload = "{}", createdAt = DateTimeOffset.UtcNow },
            CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Reports_an_unknown_message_as_not_found()
    {
        using var client = Factory.CreateApiClient();

        using var response = await client.GetAsync(
            new Uri($"/v1/messages/{Guid.CreateVersion7()}", UriKind.Relative),
            CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static MessageEnvelope Envelope(string? payload = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = "send-email",
        Payload = payload ?? """{"to":["ada@example.com"],"subject":"Your invoice","html":"<p>Invoice</p>"}""",
        CreatedAt = Factory.Clock.GetUtcNow(),
    };

    private static Task<int> RunInboxAsync() =>
        Factory.Services.GetRequiredService<InboxProcessor>().RunOnceAsync(CancellationToken.None);

    private static Task<InboxMessage?> FindAsync(Guid id) =>
        WithScopeAsync(services => services.GetRequiredService<MessagingKit.Inbox.Abstractions.IInbox>().FindAsync(id));
}
