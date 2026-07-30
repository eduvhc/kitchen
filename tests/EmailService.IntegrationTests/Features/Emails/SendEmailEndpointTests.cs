using System.Net;
using System.Net.Http.Json;
using EmailService.Features.Emails;

namespace EmailService.IntegrationTests.Features.Emails;

[TestClass]
public class SendEmailEndpointTests : ApiTest
{
    [TestMethod]
    public async Task Rejects_a_request_without_an_api_key()
    {
        using var client = Factory.CreateClientWithKey(null);

        using var response = await client.PostAsJsonAsync("/v1/emails", new { to = new[] { "ada@example.com" } }, CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Rejects_an_unknown_api_key()
    {
        using var client = Factory.CreateClientWithKey("nope");

        using var response = await client.GetAsync(new Uri("/v1/emails", UriKind.Relative), CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Queues_an_email_and_returns_created()
    {
        using var client = Factory.CreateSenderClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/emails",
            new { to = new[] { "ada@example.com" }, subject = "Hello", html = "<p>Hi</p>" },
            CancellationToken);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<EmailResponse>(CancellationToken);
        Assert.AreEqual(EmailStatus.Queued, body!.Status);
        Assert.AreEqual("Hello", body.Subject);
        Assert.AreEqual($"/v1/emails/{body.Id}", response.Headers.Location?.OriginalString);

        var stored = await WithQueueAsync(queue => queue.FindAsync(body.Id));
        Assert.AreEqual("sender", stored!.Source);
    }

    [TestMethod]
    public async Task Replays_an_idempotent_request_without_queueing_twice()
    {
        using var client = Factory.CreateSenderClient();
        var request = new
        {
            to = new[] { "ada@example.com" },
            subject = "Hello",
            text = "Hi",
            idempotencyKey = "order-99",
        };

        using var first = await client.PostAsJsonAsync("/v1/emails", request, CancellationToken);
        using var second = await client.PostAsJsonAsync("/v1/emails", request, CancellationToken);

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<EmailResponse>(CancellationToken);
        var secondBody = await second.Content.ReadFromJsonAsync<EmailResponse>(CancellationToken);
        Assert.AreEqual(firstBody!.Id, secondBody!.Id);

        var listed = await WithQueueAsync(queue => queue.ListAsync(new EmailQueryFilter()));
        Assert.HasCount(1, listed);
    }

    [TestMethod]
    public async Task Returns_a_problem_document_for_an_invalid_request()
    {
        using var client = Factory.CreateSenderClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/emails",
            new { to = Array.Empty<string>(), subject = "Hello", text = "Hi" },
            CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task Reads_one_email_back()
    {
        var queued = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));
        using var client = Factory.CreateSenderClient();

        var body = await client.GetFromJsonAsync<EmailResponse>($"/v1/emails/{queued.Id}", CancellationToken);

        Assert.AreEqual(queued.Id, body!.Id);
    }

    [TestMethod]
    public async Task Returns_not_found_for_an_unknown_id()
    {
        using var client = Factory.CreateSenderClient();

        using var response = await client.GetAsync(new Uri($"/v1/emails/{Guid.CreateVersion7()}", UriKind.Relative), CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancels_a_queued_email()
    {
        var queued = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));
        using var client = Factory.CreateSenderClient();

        using var cancelled = await client.PostAsync(new Uri($"/v1/emails/{queued.Id}/cancel", UriKind.Relative), null, CancellationToken);
        using var again = await client.PostAsync(new Uri($"/v1/emails/{queued.Id}/cancel", UriKind.Relative), null, CancellationToken);

        Assert.AreEqual(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);
    }

    [TestMethod]
    public async Task Renders_a_stored_template_when_sending()
    {
        using var admin = Factory.CreateAdminClient();
        using var upsert = await admin.PutAsJsonAsync(
            "/v1/templates/welcome",
            new { subject = "Welcome {{ name }}", html = "<p>Hi {{ name }}</p>" },
            CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, upsert.StatusCode);

        using var sender = Factory.CreateSenderClient();
        using var response = await sender.PostAsJsonAsync(
            "/v1/emails",
            new { to = new[] { "ada@example.com" }, template = "welcome", model = new { name = "Ada" } },
            CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<EmailResponse>(CancellationToken);
        Assert.AreEqual("Welcome Ada", body!.Subject);

        var stored = await WithQueueAsync(queue => queue.FindAsync(body.Id));
        Assert.AreEqual("<p>Hi Ada</p>", stored!.HtmlBody);
    }
}
