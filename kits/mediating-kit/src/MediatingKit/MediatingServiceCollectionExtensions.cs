using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediatingKit;

/// <summary>Registers the mediator.</summary>
public static class MediatingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMediator"/>, <see cref="ISender"/> and <see cref="IPublisher"/>, every handler in this
    /// project and in the projects it references, and the behaviors added in <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// The source generator replaces this call, at compile time, with one that registers the mediator it generated
    /// for the calling project; this body only runs if the generator did not. Call it once, from the project that
    /// composes the application. Calling it again from the same project does nothing; calling it from a second project
    /// throws, since each project's generated mediator only knows the handlers that project can see.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the lifetime, the notification strategy and the behaviors.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The source generator did not run, or another project already called this.</exception>
    public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        throw new InvalidOperationException(
            "AddMediator() was not replaced by MediatingKit's source generator, so there is no mediator to register. "
            + "Reference the MediatingKit package from the project that calls AddMediator() (the generator ships inside it), "
            + "use C#, and keep the 'MediatingKit.Generated' namespace in <InterceptorsNamespaces>, which the package's build props add.");
    }
}

/// <summary>Registration used by the generated <c>AddMediator()</c>. Not for direct use.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MediatorRegistration
{
    /// <summary>Registers a generated mediator, its handlers and the configured behaviors.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The caller's configuration.</param>
    /// <param name="mediatorType">The generated mediator's type.</param>
    /// <param name="factory">Creates the generated mediator.</param>
    /// <param name="registerHandlers">Registers every handler the generated mediator dispatches to.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A different generated mediator is already registered.</exception>
    public static IServiceCollection Register(
        IServiceCollection services,
        Action<MediatorOptions>? configure,
        Type mediatorType,
        Func<IServiceProvider, IMediator> factory,
        Action<IServiceCollection, ServiceLifetime> registerHandlers)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mediatorType);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(registerHandlers);

        var existing = services
            .Where(d => d.ServiceType == typeof(MediatorRegistrationMarker))
            .Select(d => d.ImplementationInstance)
            .OfType<MediatorRegistrationMarker>()
            .FirstOrDefault();

        if (existing is not null)
        {
            if (existing.MediatorType == mediatorType)
            {
                return services;
            }

            throw new InvalidOperationException(
                $"AddMediator() was already called from '{existing.MediatorType.Assembly.GetName().Name}', and now again from "
                + $"'{mediatorType.Assembly.GetName().Name}'. Each project's generated mediator only knows the handlers that "
                + "project can see, so call AddMediator() once, from the project that composes the application.");
        }

        var options = new MediatorOptions();
        configure?.Invoke(options);

        if (!Enum.IsDefined(options.Lifetime))
        {
            throw new InvalidOperationException($"'{options.Lifetime}' is not a service lifetime.");
        }

        if (!Enum.IsDefined(options.NotificationPublishing))
        {
            throw new InvalidOperationException($"'{options.NotificationPublishing}' is not a notification publishing strategy.");
        }

        var publishing = options.NotificationPublishing;
        var lifetime = options.Lifetime;

        services.AddMetrics();
        services.TryAddSingleton<MediatorTelemetry>();
        services.AddSingleton(new MediatorRegistrationMarker(mediatorType));
        services.AddSingleton(sp => new MediatorRuntime(sp.GetRequiredService<MediatorTelemetry>(), publishing));

        registerHandlers(services, lifetime);
        options.RegisterBehaviors(services);

        services.Add(new ServiceDescriptor(typeof(IMediator), factory, lifetime));
        services.Add(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), lifetime));
        services.Add(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), lifetime));

        return services;
    }
}
