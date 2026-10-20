using MailingKit.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MailingKit.Resend.UnitTests;

[TestClass]
public sealed class ResendRegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection> register, FakeHttpHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        register(services);
        services.AddHttpClient<ResendEmailSender>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static OutgoingEmail Email() => new()
    {
        FromAddress = "no-reply@example.com",
        To = ["ada@example.com"],
        Subject = "Hello",
        TextBody = "Hi",
    };

    [TestMethod]
    public async Task Binds_the_resend_section_and_configures_the_client()
    {
        var handler = new FakeHttpHandler();
        var configuration = Configuration(
            ("Resend:ApiKey", "re_test"),
            ("Resend:BaseUrl", "https://proxy.example.com/resend"),
            ("Resend:TimeoutSeconds", "12"));

        await using var provider = Build(s => s.AddResendTransport(configuration), handler);

        var options = provider.GetRequiredService<IOptions<ResendOptions>>().Value;
        Assert.AreEqual("re_test", options.ApiKey);
        Assert.AreEqual(12, options.TimeoutSeconds);

        var sender = provider.GetRequiredService<IEmailSender>();
        Assert.IsInstanceOfType<ResendEmailSender>(sender);

        var result = await sender.SendAsync(Email());
        Assert.IsTrue(result.IsSuccess);

        var request = handler.Requests.Single();
        Assert.AreEqual(new Uri("https://proxy.example.com/resend/emails"), request.Uri);
        Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
        Assert.AreEqual("re_test", request.Headers.Authorization?.Parameter);
        Assert.IsTrue(request.Headers.UserAgent.Any(p => p.Product?.Name == "MailingKit.Resend"));
    }

    [TestMethod]
    public async Task Defaults_to_the_public_api()
    {
        var handler = new FakeHttpHandler();

        await using var provider = Build(s => s.AddResendTransport(o => o.ApiKey = "re_code"), handler);

        await provider.GetRequiredService<IEmailSender>().SendAsync(Email());

        var request = handler.Requests.Single();
        Assert.AreEqual(new Uri("https://api.resend.com/emails"), request.Uri);
        Assert.AreEqual("re_code", request.Headers.Authorization?.Parameter);
    }

    [TestMethod]
    public async Task Missing_api_key_fails_startup_validation()
    {
        await using var provider = Build(s => s.AddResendTransport(Configuration()), new FakeHttpHandler());

        var ex = Assert.ThrowsExactly<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, "Resend:ApiKey is required.");
    }

    [TestMethod]
    public async Task Missing_api_key_fails_when_the_sender_is_resolved()
    {
        await using var provider = Build(s => s.AddResendTransport(o => o.ApiKey = " "), new FakeHttpHandler());

        Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IEmailSender>());
    }

    [TestMethod]
    public async Task Relative_base_url_fails_validation()
    {
        await using var provider = Build(
            s => s.AddResendTransport(o =>
            {
                o.ApiKey = "re_test";
                o.BaseUrl = new Uri("/emails", UriKind.Relative);
            }),
            new FakeHttpHandler());

        var ex = Assert.ThrowsExactly<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, "BaseUrl");
    }
}
