using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StoringKit;

/// <summary>Names and configuration paths shared by every provider's registration.</summary>
public static class ObjectStores
{
    /// <summary>
    /// The name of the store registered without one. It is also resolvable as a plain, unkeyed
    /// <see cref="IObjectStore"/>.
    /// </summary>
    public const string DefaultName = "Default";

    /// <summary>
    /// The configuration section that holds one subsection per store: <c>Storing:Default</c>, <c>Storing:Public</c>.
    /// </summary>
    public const string ConfigurationSection = "Storing";

    /// <summary>The configuration path of the store called <paramref name="name"/>: <c>Storing:{name}</c>.</summary>
    /// <param name="name">The store name.</param>
    public static string SectionPath(string name) => $"{ConfigurationSection}:{name}";
}

/// <summary>Registers object stores. Provider packages call this from their own <c>Add…Store</c> methods.</summary>
public static class StoringServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IObjectStore"/> as a singleton keyed by <paramref name="name"/>, wrapped so every operation
    /// is traced and measured under <see cref="StoringKitInstrumentation"/>. The store named
    /// <see cref="ObjectStores.DefaultName"/> is also registered unkeyed.
    /// </summary>
    /// <remarks>
    /// Resolve a named store with <c>[FromKeyedServices("Public")] IObjectStore</c> or
    /// <c>GetRequiredKeyedService&lt;IObjectStore&gt;("Public")</c>. A custom provider registered here is instrumented
    /// exactly like the built-in ones.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The store name, unique within the container.</param>
    /// <param name="provider">A short, lower-case provider name reported as <c>storingkit.provider</c>, such as <c>s3</c>.</param>
    /// <param name="factory">Creates the store. Called once, on first resolution.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A store called <paramref name="name"/> is already registered.</exception>
    public static IServiceCollection AddObjectStore(
        this IServiceCollection services,
        string name,
        string provider,
        Func<IServiceProvider, IObjectStore> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(factory);

        if (services.Any(d => d.ServiceType == typeof(IObjectStore) && d.IsKeyedService && Equals(d.ServiceKey, name)))
        {
            throw new InvalidOperationException($"An object store called '{name}' is already registered.");
        }

        services.AddMetrics();
        services.TryAddSingleton<StoringKitMetrics>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddKeyedSingleton<IObjectStore>(name, (sp, _) =>
            new InstrumentedObjectStore(factory(sp), name, provider, sp.GetRequiredService<StoringKitMetrics>()));

        if (name == ObjectStores.DefaultName)
        {
            services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IObjectStore>(ObjectStores.DefaultName));
        }

        return services;
    }
}
