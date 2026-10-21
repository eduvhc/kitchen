using Microsoft.Extensions.DependencyInjection;

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
