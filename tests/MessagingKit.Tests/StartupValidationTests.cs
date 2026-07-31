using MessagingKit.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MessagingKit.Tests;

/// <summary>
/// Validation runs before any connection is opened, so these build a provider without a database.
/// </summary>
[TestClass]
public class StartupValidationTests
{
    [TestMethod]
    public async Task Throws_when_an_in_process_message_has_no_handler()
    {
        using var provider = Build(messaging => messaging.Sends<SendEmail>());

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Validator(provider).StartAsync(TestContext.CancellationTokenSource.Token));

        StringAssert.Contains(error.Message, "send-email");
        StringAssert.Contains(error.Message, "IMessageHandler<SendEmail>");
    }

    [TestMethod]
    public async Task Passes_when_a_handler_is_registered()
    {
        using var provider = Build(messaging => messaging.Handles<SendEmail, RecordingHandler>());

        await Validator(provider).StartAsync(TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task Ignores_messages_routed_to_another_transport()
    {
        // Handled wherever it lands, so there is nothing for this host to validate.
        using var provider = Build(messaging => messaging
            .Sends<SendEmail>()
            .UseTransportFor<RecordingTransport, SendEmail>());

        await Validator(provider).StartAsync(TestContext.CancellationTokenSource.Token);
    }

    public TestContext TestContext { get; set; } = null!;

    private static ServiceProvider Build(Action<MessagingBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(options => options.UseNpgsql("Host=localhost;Database=unused"));
        services.AddSingleton<RecordingTransport>();

        configure(services.AddMessaging<TestDbContext>());

        return services.BuildServiceProvider();
    }

    private static MessagingStartupValidator Validator(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<MessagingStartupValidator>().Single();
}
