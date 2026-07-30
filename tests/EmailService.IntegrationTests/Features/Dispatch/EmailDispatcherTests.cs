using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using EmailService.Features.Dispatch;
using EmailService.Features.Emails;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EmailService.IntegrationTests.Features.Dispatch;

[TestClass]
public class EmailDispatcherTests : ApiTest
{
    [TestMethod]
    public async Task Delivers_a_queued_email_to_the_smtp_server()
    {
        var queued = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage("ada@example.com")));

        var processed = await RunDispatcherAsync(Factory);

        Assert.AreEqual(1, processed);

        var delivered = await TestHost.Smtp.WaitForMessageAsync(
            message => message.Recipients.Contains("ada@example.com"),
            CancellationToken);

        Assert.AreEqual("Integration test", delivered.Subject);

        var stored = await WithQueueAsync(queue => queue.FindAsync(queued.Id));
        Assert.AreEqual(EmailStatus.Sent, stored!.Status);
        Assert.IsNotNull(stored.ProviderMessageId);
        Assert.IsNull(stored.LastError);
    }

    [TestMethod]
    public async Task Delivers_the_rendered_template_body()
    {
        using var admin = Factory.CreateApiClient();
        using var upsert = await admin.PutAsJsonAsync(
            "/v1/templates/welcome",
            new { subject = "Welcome {{ name }}", html = "<p>Hi {{ name }}</p>" },
            CancellationToken);
        upsert.EnsureSuccessStatusCode();

        using var sender = Factory.CreateApiClient();
        using var send = await sender.PostAsJsonAsync(
            "/v1/emails",
            new { to = new[] { "grace@example.com" }, template = "welcome", model = new { name = "Grace" } },
            CancellationToken);
        send.EnsureSuccessStatusCode();

        await RunDispatcherAsync(Factory);

        var delivered = await TestHost.Smtp.WaitForMessageAsync(
            message => message.Recipients.Contains("grace@example.com"),
            CancellationToken);
        var body = await TestHost.Smtp.GetBodyAsync(delivered.Id, CancellationToken);

        Assert.AreEqual("Welcome Grace", delivered.Subject);
        Assert.Contains("Hi Grace", body.Html);
    }

    [TestMethod]
    public async Task Delivers_cc_recipients_too()
    {
        var message = NewMessage("primary@example.com");
        message.Cc.Add("copied@example.com");
        await WithQueueAsync(queue => queue.EnqueueAsync(message));

        await RunDispatcherAsync(Factory);

        var delivered = await TestHost.Smtp.WaitForMessageAsync(ct: CancellationToken);

        Assert.AreEqual("primary@example.com", delivered.To.Single().Address);
        Assert.AreEqual("copied@example.com", delivered.Cc.Single().Address);
    }

    [TestMethod]
    public async Task Leaves_a_future_email_untouched()
    {
        var queued = await WithQueueAsync(queue =>
            queue.EnqueueAsync(NewMessage(scheduledAt: Factory.Clock.GetUtcNow().AddHours(1))));

        var processed = await RunDispatcherAsync(Factory);

        Assert.AreEqual(0, processed);

        var stored = await WithQueueAsync(queue => queue.FindAsync(queued.Id));
        Assert.AreEqual(EmailStatus.Queued, stored!.Status);
        Assert.IsEmpty(await TestHost.Smtp.GetMessagesAsync(CancellationToken));
    }

    [TestMethod]
    public async Task Requeues_the_email_when_the_smtp_server_is_unreachable()
    {
        var queued = await WithQueueAsync(queue => queue.EnqueueAsync(NewMessage()));

        await using var broken = new UnreachableSmtpFactory(FindUnusedPort());
        broken.Clock.SetUtcNow(Factory.Clock.GetUtcNow());

        var processed = await RunDispatcherAsync(broken);

        Assert.AreEqual(1, processed);

        var stored = await WithQueueAsync(queue => queue.FindAsync(queued.Id));
        Assert.AreEqual(EmailStatus.Queued, stored!.Status);
        Assert.AreEqual(1, stored.AttemptCount);
        Assert.IsNotNull(stored.LastError);
        Assert.IsGreaterThan(Factory.Clock.GetUtcNow(), stored.ScheduledAt);
        Assert.IsEmpty(await TestHost.Smtp.GetMessagesAsync(CancellationToken));
    }

    private static Task<int> RunDispatcherAsync(EmailServiceFactory factory) =>
        factory.Services.GetRequiredService<EmailDispatcher>().RunOnceAsync(CancellationToken.None);

    private static int FindUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class UnreachableSmtpFactory(int port) : EmailServiceFactory(TestHost.Environment)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Smtp:Port", port.ToString());
            builder.UseSetting("Smtp:TimeoutSeconds", "2");
        }
    }
}
