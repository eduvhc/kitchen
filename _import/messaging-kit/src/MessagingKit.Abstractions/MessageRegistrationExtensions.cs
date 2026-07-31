using Microsoft.Extensions.DependencyInjection;

namespace MessagingKit;

/// <summary>
/// Registration a module can do without knowing the host's <c>DbContext</c>. The host calls
/// <c>AddMessaging&lt;TContext&gt;</c> once; modules only declare what they send and handle, so a
/// module depends on <c>MessagingKit.Abstractions</c> alone.
/// </summary>
public static class MessageRegistrationExtensions
{
    /// <summary>Declares that this module handles <typeparamref name="TMessage"/>.</summary>
    public static IServiceCollection AddMessageHandler<TMessage, THandler>(this IServiceCollection services)
        where THandler : class, IMessageHandler<TMessage>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMessageContract<TMessage>();
        services.AddScoped<IMessageHandler<TMessage>, THandler>();

        return services;
    }

    /// <summary>
    /// Declares a message this module sends but does not handle. Only needed when nothing in this
    /// host handles the type — <see cref="AddMessageHandler{TMessage, THandler}"/> already covers it.
    /// </summary>
    public static IServiceCollection AddMessageContract<TMessage>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMessageTypeRegistration>(
            new MessageTypeRegistration(MessageName.For<TMessage>(), typeof(TMessage)));

        return services;
    }
}
