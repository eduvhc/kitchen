using MessagingKit.InProcess;
using MessagingKit.Inbox;
using MessagingKit.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox, the inbox, and in-process delivery against the host's
    /// <typeparamref name="TContext"/>. Safe to call once per module — the shared processors are
    /// registered only once.
    /// </summary>
    public static MessagingBuilder AddMessaging<TContext>(
        this IServiceCollection services,
        IConfiguration? configuration = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var outbox = services.AddOutbox<TContext>(configuration);
        services.AddInbox<TContext>(configuration);

        // Modules in one host deliver to each other's inbox by default. Override per message type
        // with UseTransport<T>("some-type") once a module moves out of process.
        outbox.UseInProcessTransport();

        // Registered once no matter how many modules call AddMessaging.
        if (!services.Any(d => d.ImplementationType == typeof(MessagingStartupValidator)))
        {
            services.AddHostedService<MessagingStartupValidator>();
        }

        return new MessagingBuilder(services, outbox);
    }
}

public sealed class MessagingBuilder(IServiceCollection services, OutboxBuilder outbox)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Declares that this module handles <typeparamref name="TMessage"/>. Registers the same name on
    /// both sides, so sender and handler cannot drift apart.
    /// </summary>
    public MessagingBuilder Handles<TMessage, THandler>()
        where THandler : class, IMessageHandler<TMessage>
    {
        Services.AddMessageHandler<TMessage, THandler>();
        return this;
    }

    /// <summary>
    /// Declares that this module sends <typeparamref name="TMessage"/> without handling it. Only
    /// needed when no module in this host handles the type — otherwise <c>Handles</c> covers it.
    /// </summary>
    public MessagingBuilder Sends<TMessage>()
    {
        Services.AddMessageContract<TMessage>();
        return this;
    }

    /// <summary>
    /// Routes specific message types through a transport. With no type names it becomes the default,
    /// replacing in-process delivery.
    /// </summary>
    public MessagingBuilder UseTransport<TTransport>(params string[] messageTypes)
        where TTransport : class, IMessageTransport
    {
        outbox.UseTransport<TTransport>(messageTypes);
        return this;
    }

    /// <summary>Routes the given message types through a transport, naming them by type.</summary>
    public MessagingBuilder UseTransportFor<TTransport, TMessage>()
        where TTransport : class, IMessageTransport
    {
        outbox.UseTransport<TTransport>(MessageName.For<TMessage>());
        return this;
    }

    /// <summary>
    /// Keeps the named message types in this host once another transport has taken over as the
    /// default. In-process delivery is already the default, so this is only needed alongside
    /// <c>UseTransport</c>.
    /// </summary>
    public MessagingBuilder UseInProcessTransport(params string[] messageTypes)
    {
        outbox.UseInProcessTransport(messageTypes);
        return this;
    }
}
