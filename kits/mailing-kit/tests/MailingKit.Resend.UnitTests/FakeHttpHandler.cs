using System.Net;
using System.Text;

namespace MailingKit.Resend.UnitTests;

/// <summary>Records each request and answers with whatever <see cref="Respond"/> returns.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"id":"re_123"}"""));

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, request.Headers, body));
        return await Respond(request, cancellationToken);
    }
}

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri? Uri,
    System.Net.Http.Headers.HttpRequestHeaders Headers,
    string? Body);
