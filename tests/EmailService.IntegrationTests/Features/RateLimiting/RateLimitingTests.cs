using EmailService.IntegrationTests.Infrastructure;
using System.Net.Http.Json;
using System.Net;

namespace EmailService.IntegrationTests.Features.RateLimiting;

[TestClass]
public class RateLimitingTests : ApiTest
{
    private const string LimitedSource = "limited-source";

    [TestMethod]
    public async Task Rejects_a_source_past_its_permit_limit()
    {
        using var client = Factory.CreateApiClient(LimitedSource);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await SendAsync(client, $"burst{i}@example.com");
            statuses.Add(response.StatusCode);
        }

        Assert.AreEqual(3, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.AreEqual(HttpStatusCode.TooManyRequests, statuses[3]);
    }

    [TestMethod]
    public async Task Answers_a_rejected_request_with_retry_after()
    {
        using var client = Factory.CreateApiClient(LimitedSource);

        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 5 && rejected is null; i++)
        {
            var response = await SendAsync(client, $"retry{i}@example.com");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
            }
            else
            {
                response.Dispose();
            }
        }

        Assert.IsNotNull(rejected);
        Assert.IsNotNull(rejected.Headers.RetryAfter);
        rejected.Dispose();
    }

    [TestMethod]
    public async Task Keeps_one_sources_limit_away_from_another()
    {
        using var limited = Factory.CreateApiClient(LimitedSource);
        for (var i = 0; i < 4; i++)
        {
            using var _ = await SendAsync(limited, $"noisy{i}@example.com");
        }

        using var other = Factory.CreateApiClient("quiet-source");
        using var response = await SendAsync(other, "quiet@example.com");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task Leaves_health_checks_unlimited()
    {
        using var client = Factory.CreateApiClient(LimitedSource);

        for (var i = 0; i < 4; i++)
        {
            using var _ = await SendAsync(client, $"flood{i}@example.com");
        }

        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(HttpClient client, string to) =>
        client.PostAsJsonAsync(
            "/v1/emails",
            new { to = new[] { to }, subject = "Rate limit", text = "Body" },
            CancellationToken);
}
