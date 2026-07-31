using MessagingKit.Inbox;
using MessagingKit.Outbox;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MessagingKit.UnitTests;

/// <summary>
/// Modules register without naming the host's DbContext, and several modules in one host must not
/// each spawn their own background loops.
/// </summary>
[TestClass]
public class ModuleRegistrationTests
{
    [TestMethod]
    public void A_module_registers_a_handler_without_naming_the_context()
    {
        var services = new ServiceCollection();
        services.AddMessageHandler<SendEmail, RecordingHandler>();

        using var provider = services.BuildServiceProvider();

        var registration = provider.GetServices<IMessageTypeRegistration>().Single();
        Assert.AreEqual("send-email", registration.Name);
        Assert.AreEqual(typeof(SendEmail), registration.MessageType);

        Assert.IsTrue(provider.GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(IMessageHandler<SendEmail>)));
    }

    [TestMethod]
    public void A_module_declares_a_contract_it_only_sends()
    {
        var services = new ServiceCollection();
        services.AddMessageContract<SendEmail>();

        using var provider = services.BuildServiceProvider();

        Assert.AreEqual("send-email", provider.GetServices<IMessageTypeRegistration>().Single().Name);
        Assert.IsFalse(provider.GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(IMessageHandler<SendEmail>)));
    }

    [TestMethod]
    public void Several_modules_share_one_dispatcher_and_one_processor()
    {
        var services = Base();

        services.AddMessaging<TestDbContext>();
        services.AddMessaging<TestDbContext>();
        services.AddMessaging<TestDbContext>();

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();

        Assert.HasCount(1, hosted.OfType<OutboxDispatcher>());
        Assert.HasCount(1, hosted.OfType<InboxProcessor>());
        Assert.HasCount(
            1,
            hosted.Where(h => h.GetType().Name == "MessagingStartupValidator"),
            "the validator should be registered once no matter how many modules call AddMessaging");
    }

    [TestMethod]
    public void Registration_order_between_host_and_module_does_not_matter()
    {
        var moduleFirst = Base();
        moduleFirst.AddMessageHandler<SendEmail, RecordingHandler>();
        moduleFirst.AddMessaging<TestDbContext>();

        var hostFirst = Base();
        hostFirst.AddMessaging<TestDbContext>();
        hostFirst.AddMessageHandler<SendEmail, RecordingHandler>();

        using var a = moduleFirst.BuildServiceProvider();
        using var b = hostFirst.BuildServiceProvider();

        foreach (var provider in new[] { a, b })
        {
            Assert.IsTrue(provider.GetRequiredService<IServiceProviderIsService>()
                .IsService(typeof(IMessageHandler<SendEmail>)));
            Assert.AreEqual("send-email", provider.GetServices<IMessageTypeRegistration>().Single().Name);
        }
    }

    [TestMethod]
    public void Handles_and_AddMessageHandler_produce_the_same_registration()
    {
        var viaBuilder = Base();
        viaBuilder.AddMessaging<TestDbContext>().Handles<SendEmail, RecordingHandler>();

        var viaModule = Base();
        viaModule.AddMessaging<TestDbContext>();
        viaModule.AddMessageHandler<SendEmail, RecordingHandler>();

        using var a = viaBuilder.BuildServiceProvider();
        using var b = viaModule.BuildServiceProvider();

        Assert.AreEqual(
            a.GetServices<IMessageTypeRegistration>().Single().Name,
            b.GetServices<IMessageTypeRegistration>().Single().Name);
    }

    private static ServiceCollection Base()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HandlerLog>();
        services.AddDbContext<TestDbContext>(b => b.UseNpgsql("Host=localhost;Database=unused"));
        return services;
    }
}
