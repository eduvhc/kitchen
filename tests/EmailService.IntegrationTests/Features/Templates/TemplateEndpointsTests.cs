using EmailService.Features.Templates.Contracts;
using EmailService.Features.Templates.PreviewTemplate;
using EmailService.IntegrationTests.Infrastructure;
using System.Net.Http.Json;
using System.Net;

namespace EmailService.IntegrationTests.Features.Templates;

[TestClass]
public class TemplateEndpointTests : ApiTest
{
    [TestMethod]
    public async Task Creates_then_updates_a_template()
    {
        using var client = Factory.CreateApiClient();

        using var created = await client.PutAsJsonAsync(
            "/v1/templates/receipt",
            new { subject = "Receipt {{ number }}", html = "<p>{{ number }}</p>", description = "Order receipt" },
            CancellationToken);

        using var updated = await client.PutAsJsonAsync(
            "/v1/templates/receipt",
            new { subject = "Your receipt {{ number }}", text = "{{ number }}" },
            CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);

        var body = await updated.Content.ReadFromJsonAsync<TemplateResponse>(CancellationToken);
        Assert.AreEqual("Your receipt {{ number }}", body!.Subject);
        Assert.IsNull(body.Html);

        var all = await client.GetFromJsonAsync<List<TemplateResponse>>("/v1/templates", CancellationToken);
        Assert.HasCount(1, all!);
    }

    [TestMethod]
    public async Task Rejects_a_template_that_does_not_parse()
    {
        using var client = Factory.CreateApiClient();

        using var response = await client.PutAsJsonAsync(
            "/v1/templates/broken",
            new { subject = "{{ if }}" },
            CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Previews_a_template_without_sending()
    {
        using var client = Factory.CreateApiClient();
        using var upsert = await client.PutAsJsonAsync(
            "/v1/templates/welcome",
            new { subject = "Welcome {{ name }}", html = "<p>Hi {{ name }}</p>" },
            CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, upsert.StatusCode);

        using var response = await client.PostAsJsonAsync(
            "/v1/templates/welcome/preview",
            new { model = new { name = "Ada" } },
            CancellationToken);

        var preview = await response.Content.ReadFromJsonAsync<PreviewTemplateResponse>(CancellationToken);
        Assert.AreEqual("Welcome Ada", preview!.Subject);
        Assert.AreEqual("<p>Hi Ada</p>", preview.Html);

        var queued = await WithQueueAsync(queue => queue.ListAsync(new()));
        Assert.IsEmpty(queued);
    }

    [TestMethod]
    public async Task Deletes_a_template()
    {
        using var client = Factory.CreateApiClient();
        using var upsert = await client.PutAsJsonAsync("/v1/templates/gone", new { subject = "Bye" }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, upsert.StatusCode);

        using var deleted = await client.DeleteAsync(new Uri("/v1/templates/gone", UriKind.Relative), CancellationToken);
        using var again = await client.DeleteAsync(new Uri("/v1/templates/gone", UriKind.Relative), CancellationToken);

        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode);
    }
}
