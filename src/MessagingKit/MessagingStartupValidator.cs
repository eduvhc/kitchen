using MessagingKit.InProcess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MessagingKit;

/// <summary>
/// Fails the host at boot when a message is delivered in-process but nothing here handles it.
/// Without this the first such message dead-letters in production, hours after the deploy that
/// caused it.
/// </summary>
internal sealed class MessagingStartupValidator(
    IServiceProvider provider,
    IEnumerable<IMessageTypeRegistration> registrations,
    IEnumerable<TransportRegistration> transports) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var transportList = transports.ToList();
        var defaultTransport = transportList.Find(t => t.Key is null)?.TransportType;

        var routed = transportList
            .Where(t => t.Key is not null)
            .GroupBy(t => t.Key!)
            .ToDictionary(g => g.Key, g => g.Last().TransportType);

        // Asks whether a handler is registered without constructing one — activating every handler at
        // boot would run their constructors for nothing, and a handler with a missing dependency
        // would surface as a DI error rather than the message below.
        var isService = provider.GetRequiredService<IServiceProviderIsService>();
        var problems = new List<string>();

        foreach (var registration in registrations.DistinctBy(r => r.Name))
        {
            var transport = routed.GetValueOrDefault(registration.Name) ?? defaultTransport;

            // Anything leaving this host is handled wherever it lands, so there is nothing to check.
            if (transport != typeof(InProcessTransport))
            {
                continue;
            }

            var handlerType = typeof(IMessageHandler<>).MakeGenericType(registration.MessageType);

            if (!isService.IsService(handlerType))
            {
                problems.Add(
                    $"'{registration.Name}' is delivered in-process but no IMessageHandler<{registration.MessageType.Name}> is registered. " +
                    $"Register a handler with Handles<{registration.MessageType.Name}, THandler>(), or route it to another transport.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "MessagingKit is misconfigured:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
