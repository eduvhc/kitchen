using System.Net;
using System.Text.Json;
using MailingKit.Domain;
using MailingKit.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using ResultingKit;

namespace MailingKit.Resend.UnitTests;

[TestClass]
public sealed class ResendEmailSenderTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();

    public void Dispose() => _handler.Dispose();

    private ResendEmailSender CreateSender(TimeSpan? timeout = null)
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("https://api.resend.com/") };
        if (timeout is { } t)
        {
            http.Timeout = t;
        }

        return new ResendEmailSender(http, NullLogger<ResendEmailSender>.Instance);
    }

    private static OutgoingEmail Minimal() => new()
    {
        FromAddress = "no-reply@example.com",
        To = ["ada@example.com"],
        Subject = "Hello",
        TextBody = "Hi",
    };

    private static OutgoingEmail Full() => new()
    {
        FromAddress = "billing@example.com",
        FromName = "Billing",
        ReplyTo = "support@example.com",
        To = ["ada@example.com", "grace@example.com"],
        Cc = ["cc@example.com"],
        Bcc = ["bcc@example.com"],
        Subject = "Your invoice",
        HtmlBody = "<p>Hi <img src=\"cid:logo\"></p>",
        TextBody = "Hi",
        Headers = new Dictionary<string, string> { ["X-Campaign"] = "invoice" },
        Attachments =
        [
            new EmailAttachment { FileName = "invoice.pdf", ContentType = "application/pdf", Content = "JVBERi0=" },
            new EmailAttachment { FileName = "logo.png", ContentType = "image/png", Content = "iVBORw0=", ContentId = "logo" },
        ],
    };

    [TestMethod]
    public async Task Posts_to_emails_endpoint()
    {
        await CreateSender().SendAsync(Minimal());

        var request = _handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual(new Uri("https://api.resend.com/emails"), request.Uri);
    }

    [TestMethod]
    public async Task Maps_every_field_of_the_email()
    {
        await CreateSender().SendAsync(Full());

        using var json = JsonDocument.Parse(_handler.Requests.Single().Body!);
        var root = json.RootElement;

        Assert.AreEqual("Billing <billing@example.com>", root.GetProperty("from").GetString());
        CollectionAssert.AreEqual(new[] { "ada@example.com", "grace@example.com" }, Strings(root.GetProperty("to")));
        CollectionAssert.AreEqual(new[] { "cc@example.com" }, Strings(root.GetProperty("cc")));
        CollectionAssert.AreEqual(new[] { "bcc@example.com" }, Strings(root.GetProperty("bcc")));
        Assert.AreEqual("support@example.com", root.GetProperty("reply_to").GetString());
        Assert.AreEqual("Your invoice", root.GetProperty("subject").GetString());
        Assert.AreEqual("<p>Hi <img src=\"cid:logo\"></p>", root.GetProperty("html").GetString());
        Assert.AreEqual("Hi", root.GetProperty("text").GetString());
        Assert.AreEqual("invoice", root.GetProperty("headers").GetProperty("X-Campaign").GetString());

        var attachments = root.GetProperty("attachments").EnumerateArray().ToList();
        Assert.HasCount(2, attachments);

        Assert.AreEqual("JVBERi0=", attachments[0].GetProperty("content").GetString());
        Assert.AreEqual("invoice.pdf", attachments[0].GetProperty("filename").GetString());
        Assert.AreEqual("application/pdf", attachments[0].GetProperty("content_type").GetString());
        Assert.IsFalse(attachments[0].TryGetProperty("content_id", out _));

        Assert.AreEqual("iVBORw0=", attachments[1].GetProperty("content").GetString());
        Assert.AreEqual("logo.png", attachments[1].GetProperty("filename").GetString());
        Assert.AreEqual("image/png", attachments[1].GetProperty("content_type").GetString());
        Assert.AreEqual("logo", attachments[1].GetProperty("content_id").GetString());
    }

    [TestMethod]
    public async Task Omits_empty_optional_fields()
    {
        await CreateSender().SendAsync(Minimal());

        using var json = JsonDocument.Parse(_handler.Requests.Single().Body!);
        var root = json.RootElement;

        Assert.AreEqual("no-reply@example.com", root.GetProperty("from").GetString());
        foreach (var name in new[] { "cc", "bcc", "reply_to", "html", "headers", "attachments" })
        {
            Assert.IsFalse(root.TryGetProperty(name, out _), $"'{name}' should be omitted");
        }
    }

    [TestMethod]
    public async Task Sends_no_idempotency_key_because_the_email_carries_no_stable_id()
    {
        await CreateSender().SendAsync(Minimal());

        Assert.IsFalse(_handler.Requests.Single().Headers.Contains("Idempotency-Key"));
    }

    [TestMethod]
    [DataRow("Billing", "Billing <a@example.com>")]
    [DataRow("Acme, Inc.", "\"Acme, Inc.\" <a@example.com>")]
    [DataRow("Say \"hi\"", "\"Say \\\"hi\\\"\" <a@example.com>")]
    [DataRow(null, "a@example.com")]
    [DataRow(" ", "a@example.com")]
    public void Formats_the_sender(string? name, string expected) =>
        Assert.AreEqual(expected, ResendEmailSender.FormatMailbox("a@example.com", name));

    [TestMethod]
    public async Task Success_returns_the_provider_id()
    {
        var result = await CreateSender().SendAsync(Minimal());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("re_123", result.Value.ProviderMessageId);
    }

    [TestMethod]
    public async Task Success_with_unreadable_body_still_succeeds()
    {
        _handler.Respond = (_, _) => Task.FromResult(FakeHttpHandler.Text(HttpStatusCode.OK, "ok"));

        var result = await CreateSender().SendAsync(Minimal());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value.ProviderMessageId);
    }

    [TestMethod]
    [DataRow(422, "validation_error", ErrorKind.Validation)]
    [DataRow(400, "validation_error", ErrorKind.Failure)]
    [DataRow(401, "missing_api_key", ErrorKind.Unauthorized)]
    [DataRow(403, "invalid_permission", ErrorKind.Forbidden)]
    [DataRow(404, "not_found", ErrorKind.Failure)]
    [DataRow(405, "method_not_allowed", ErrorKind.Failure)]
    [DataRow(409, "invalid_idempotent_request", ErrorKind.Conflict)]
    [DataRow(409, "concurrent_idempotent_requests", ErrorKind.Transient)]
    [DataRow(429, "rate_limit_exceeded", ErrorKind.Transient)]
    [DataRow(500, "application_error", ErrorKind.Transient)]
    [DataRow(503, "service_unavailable", ErrorKind.Transient)]
    public async Task Error_responses_map_to_kinds(int status, string name, ErrorKind kind)
    {
        _handler.Respond = (_, _) => Task.FromResult(FakeHttpHandler.Json(
            (HttpStatusCode)status,
            $$"""{"statusCode":{{status}},"name":"{{name}}","message":"Something about {{name}}"}"""));

        var result = await CreateSender().SendAsync(Minimal());

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(kind, result.Error!.Kind);
        Assert.AreEqual($"resend.{name}", result.Error.Code);
        Assert.AreEqual($"{status}: Something about {name}", result.Error.Message);
    }

    [TestMethod]
    public async Task Non_json_error_body_keeps_the_raw_text()
    {
        _handler.Respond = (_, _) => Task.FromResult(FakeHttpHandler.Text(HttpStatusCode.BadGateway, "<html>Bad gateway</html>"));

        var result = await CreateSender().SendAsync(Minimal());

        Assert.AreEqual(ErrorKind.Transient, result.Error!.Kind);
        Assert.AreEqual("resend.http_502", result.Error.Code);
        Assert.AreEqual("502: <html>Bad gateway</html>", result.Error.Message);
    }

    [TestMethod]
    public async Task Empty_error_body_uses_the_status()
    {
        _handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await CreateSender().SendAsync(Minimal());

        Assert.AreEqual(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.AreEqual("resend.http_403", result.Error.Code);
        Assert.AreEqual("403: Forbidden", result.Error.Message);
    }

    [TestMethod]
    public async Task Long_non_json_error_body_is_truncated()
    {
        _handler.Respond = (_, _) => Task.FromResult(FakeHttpHandler.Text(HttpStatusCode.BadRequest, new string('x', 2000)));

        var result = await CreateSender().SendAsync(Minimal());

        Assert.AreEqual(ErrorKind.Failure, result.Error!.Kind);
        Assert.IsLessThan(600, result.Error.Message.Length);
    }

    [TestMethod]
    public async Task Timeout_is_transient()
    {
        _handler.Respond = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };

        var result = await CreateSender(TimeSpan.FromMilliseconds(50)).SendAsync(Minimal());

        Assert.AreEqual(ErrorKind.Transient, result.Error!.Kind);
        Assert.AreEqual("resend.timeout", result.Error.Code);
    }

    [TestMethod]
    public async Task Network_failure_is_transient()
    {
        _handler.Respond = (_, _) => throw new HttpRequestException("No such host is known.");

        var result = await CreateSender().SendAsync(Minimal());

        Assert.AreEqual(ErrorKind.Transient, result.Error!.Kind);
        Assert.AreEqual("resend.unavailable", result.Error.Code);
        StringAssert.Contains(result.Error.Message, "No such host is known.");
    }

    [TestMethod]
    public async Task Caller_cancellation_throws()
    {
        using var cts = new CancellationTokenSource();
        _handler.Respond = async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateSender().SendAsync(Minimal(), cts.Token));
    }

    [TestMethod]
    public async Task Already_cancelled_token_throws_without_a_request()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateSender().SendAsync(Minimal(), cts.Token));
        Assert.IsEmpty(_handler.Requests);
    }

    private static string?[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()).ToArray();
}
