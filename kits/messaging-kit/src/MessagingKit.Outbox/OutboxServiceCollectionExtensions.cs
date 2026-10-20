using MessagingKit.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessagingKit.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static OutboxBuilder AddOutbox<TContext>(this IServiceCollection services, IConfiguration? configuration = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configuration is not null)
        {
            services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        }
        else
        {
            services.AddOptions<OutboxOptions>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.TryAddSingleton<MessageTypeRegistry>();

        services.TryAddSingleton<OutboxSignal>();

        // IMeterFactory comes with a generic host; AddMetrics makes it exist without one too.
        services.AddMetrics();
        services.TryAddSingleton<OutboxMetrics>();
        services.TryAddScoped<IOutboxBacklog, OutboxBacklog<TContext>>();

        services.TryAddScoped<IOutbox, Outbox<TContext>>();
        services.TryAddScoped<IOutboxStore, OutboxStore<TContext>>();
        services.TryAddScoped<IMessageTransportResolver, MessageTransportResolver>();

        // Same reasoning as the inbox: several modules may each call AddOutbox in one host.
        if (!services.Any(d => d.ServiceType == typeof(OutboxDispatcher)))
        {
            // A factory, because the container only sees public constructors and the metrics are internal.
            services.AddSingleton(sp => new OutboxDispatcher(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<OutboxOptions>>(),
                sp.GetRequiredService<OutboxSignal>(),
                sp.GetRequiredService<ILogger<OutboxDispatcher>>(),
                sp.GetRequiredService<OutboxMetrics>(),
                sp.GetRequiredService<TimeProvider>()));
            services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
        }

        return new OutboxBuilder(services);
    }
}

public sealed class OutboxBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public OutboxBuilder AddMessage<TMessage>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Services.AddSingleton<IMessageTypeRegistration>(new MessageTypeRegistration(name, typeof(TMessage)));
        return this;
    }

    /// <summary>Names the message by <see cref="MessageAttribute"/>, or kebab-case of the type name.</summary>
    public OutboxBuilder AddMessage<TMessage>() => AddMessage<TMessage>(MessageName.For<TMessage>());

    /// <summary>
    /// Registers a transport. Each key matches either a message's destination or its type name, with
    /// destination taking precedence; pass no keys to make it the default for everything unrouted.
    /// </summary>
    /// <example>
    /// <code>
    /// .UseInProcessTransport("send-email")   // this type goes to the local inbox
    /// .UseTransport&lt;BrokerTransport&gt;()       // everything else goes to the broker
    /// </code>
    /// </example>
    public OutboxBuilder UseTransport<TTransport>(params string[] keys)
        where TTransport : class, IMessageTransport
    {
        ArgumentNullException.ThrowIfNull(keys);

        Services.TryAddScoped<TTransport>();

        if (keys.Length == 0)
        {
            Services.AddSingleton(new TransportRegistration(null, typeof(TTransport)));

            // Keeps GetRequiredService<IMessageTransport>() resolving for callers that predate routing.
            Services.TryAddScoped<IMessageTransport>(sp => sp.GetRequiredService<TTransport>());
            return this;
        }

        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            Services.AddSingleton(new TransportRegistration(key, typeof(TTransport)));
        }

        return this;
    }
}
