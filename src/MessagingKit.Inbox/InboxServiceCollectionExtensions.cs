using MessagingKit.Inbox.Abstractions;
using MessagingKit.Inbox.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MessagingKit.Inbox;

public static class InboxServiceCollectionExtensions
{
    public static InboxBuilder AddInbox<TContext>(this IServiceCollection services, IConfiguration? configuration = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configuration is not null)
        {
            services.Configure<InboxOptions>(configuration.GetSection(InboxOptions.SectionName));
        }
        else
        {
            services.AddOptions<InboxOptions>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.TryAddSingleton<MessageTypeRegistry>();

        services.AddScoped<IInbox, Inbox<TContext>>();
        services.AddScoped<IInboxStore, InboxStore<TContext>>();
        services.AddSingleton<InboxProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<InboxProcessor>());

        return new InboxBuilder(services);
    }
}

public sealed class InboxBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public InboxBuilder AddHandler<TMessage, THandler>(string name)
        where THandler : class, IMessageHandler<TMessage>
    {
        Services.AddSingleton<IMessageTypeRegistration>(new MessageTypeRegistration(name, typeof(TMessage)));
        Services.AddScoped<IMessageHandler<TMessage>, THandler>();
        return this;
    }
}
