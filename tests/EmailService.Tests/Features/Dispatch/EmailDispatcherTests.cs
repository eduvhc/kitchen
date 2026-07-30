using EmailService.Features.Dispatch;
using EmailService.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmailService.Tests.Features.Dispatch;

[TestClass]
public class EmailDispatcherTests
{
    private static EmailDispatcher CreateDispatcher(DispatcherOptions options)
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        return new EmailDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<EmailDispatcher>.Instance);
    }

    [TestMethod]
    [DataRow(1, 30)]
    [DataRow(2, 60)]
    [DataRow(3, 120)]
    [DataRow(4, 240)]
    public void Backs_off_exponentially(int attempt, int expectedSeconds)
    {
        var dispatcher = CreateDispatcher(new DispatcherOptions
        {
            BaseRetryDelaySeconds = 30,
            MaxRetryDelaySeconds = 3600,
        });

        Assert.AreEqual(expectedSeconds, dispatcher.BackoffFor(attempt).TotalSeconds);
    }

    [TestMethod]
    public void Caps_the_backoff_at_the_configured_maximum()
    {
        var dispatcher = CreateDispatcher(new DispatcherOptions
        {
            BaseRetryDelaySeconds = 30,
            MaxRetryDelaySeconds = 300,
        });

        Assert.AreEqual(300, dispatcher.BackoffFor(12).TotalSeconds);
    }
}
