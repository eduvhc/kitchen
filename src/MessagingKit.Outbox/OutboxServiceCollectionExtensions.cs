using MessagingKit.Outbox.Abstractions;
using MessagingKit.Outbox.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddScoped<IOutbox, Outbox<TContext>>();
        services.AddScoped<IOutboxStore, OutboxStore<TContext>>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

        return new OutboxBuilder(services);
    }
}

public sealed class OutboxBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public OutboxBuilder AddMessage<TMessage>(string name)
    {
        Services.AddSingleton<IMessageTypeRegistration>(new MessageTypeRegistration(name, typeof(TMessage)));
        return this;
    }

    public OutboxBuilder UseTransport<TTransport>()
        where TTransport : class, IMessageTransport
    {
        Services.AddScoped<IMessageTransport, TTransport>();
        return this;
    }
}
