using System.Net.Mail;
using TestingKit.MSTest;

namespace TestingKit.Tests;

[TestClass]
public class SmtpFixtureTests : IntegrationTest
{
    protected override TestEnvironment Environment => TestHost.Environment;

    [TestMethod]
    public async Task Captures_a_sent_message()
    {
        await SendAsync("ada@example.com", "Hello Ada", "<p>Hi</p>");

        var message = await TestHost.Smtp.WaitForMessageAsync(ct: CancellationToken);

        Assert.AreEqual("Hello Ada", message.Subject);
        Assert.Contains("ada@example.com", message.Recipients);
    }

    [TestMethod]
    public async Task Waits_for_a_specific_recipient()
    {
        await SendAsync("first@example.com", "One", "<p>1</p>");
        await SendAsync("second@example.com", "Two", "<p>2</p>");

        var message = await TestHost.Smtp.WaitForMessageAsync(
            m => m.Recipients.Contains("second@example.com"),
            CancellationToken);

        Assert.AreEqual("Two", message.Subject);
    }

    [TestMethod]
    public async Task Reads_the_rendered_body()
    {
        await SendAsync("ada@example.com", "Body test", "<p>Hi Ada</p>");

        var message = await TestHost.Smtp.WaitForMessageAsync(ct: CancellationToken);
        var body = await TestHost.Smtp.GetBodyAsync(message.Id, CancellationToken);

        Assert.Contains("Hi Ada", body.Html);
    }

    [TestMethod]
    public async Task Reset_empties_the_inbox()
    {
        await SendAsync("ada@example.com", "To be cleared", "<p>x</p>");
        await TestHost.Smtp.WaitForMessageAsync(ct: CancellationToken);

        await TestHost.Smtp.ResetAsync(CancellationToken);

        Assert.IsEmpty(await TestHost.Smtp.GetMessagesAsync(CancellationToken));
    }

    [TestMethod]
    public async Task Times_out_when_no_message_arrives()
    {
        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            TestHost.Smtp.WaitForMessageAsync(m => m.Subject == "never sent", CancellationToken));
    }

    private static async Task SendAsync(string to, string subject, string html)
    {
        using var client = new SmtpClient(TestHost.Smtp.Host, TestHost.Smtp.SmtpPort);
        using var message = new MailMessage("no-reply@example.com", to, subject, html) { IsBodyHtml = true };
        await client.SendMailAsync(message);
    }
}
