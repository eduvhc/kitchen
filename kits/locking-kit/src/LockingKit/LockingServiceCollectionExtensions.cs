using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LockingKit;

/// <summary>Names, limits and configuration paths shared by every provider's registration.</summary>
public static class DistributedLocks
{
    /// <summary>
    /// The name of the lock provider registered without one. It is also resolvable as a plain, unkeyed
    /// <see cref="IDistributedLock"/>.
    /// </summary>
    public const string DefaultName = "Default";

    /// <summary>The configuration section that holds one subsection per provider: <c>Locking:Default</c>, <c>Locking:Jobs</c>.</summary>
    public const string ConfigurationSection = "Locking";

    /// <summary>The longest key, in UTF-16 characters, that every provider accepts.</summary>
    public const int MaxKeyLength = 512;

    /// <summary>The configuration path of the provider called <paramref name="name"/>: <c>Locking:{name}</c>.</summary>
    /// <param name="name">The provider name.</param>
    public static string SectionPath(string name) => $"{ConfigurationSection}:{name}";
}

/// <summary>Registers lock providers.</summary>
public static class LockingServiceCollectionExtensions
{
    /// <summary>
    /// Starts registering a lock provider called <paramref name="name"/>; pick its backend on the returned builder
    /// with <c>UseAzureBlobLeases</c>, <c>UsePostgres</c> or <see cref="InMemoryLockingBuilderExtensions.UseInMemory"/>.
    /// </summary>
    /// <remarks>
    /// The provider is an <see cref="IDistributedLock"/> singleton keyed by <paramref name="name"/>; the one called
    /// <see cref="DistributedLocks.DefaultName"/> is also registered unkeyed. Resolve a named one with
    /// <c>[FromKeyedServices("Jobs")] IDistributedLock</c>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The provider name, unique within the container.</param>
    /// <returns>A builder for the provider's backend.</returns>
    public static LockingBuilder AddLocking(this IServiceCollection services, string name = DistributedLocks.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new LockingBuilder(services, name);
    }
}

/// <summary>Picks the backend of one lock provider. Provider packages add <c>Use…</c> extensions to it.</summary>
public sealed class LockingBuilder
{
    internal LockingBuilder(IServiceCollection services, string name)
    {
        Services = services;
        Name = name;
    }

    /// <summary>The service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The provider name, and the service key it is registered under.</summary>
    public string Name { get; }

    /// <summary>
    /// Registers <see cref="Name"/> over a backend. Provider packages call this from their <c>Use…</c> methods; call it
    /// directly to plug in your own <see cref="ILockBackend"/>, which is then instrumented and behaves like the built-in
    /// ones. The engine settings on <typeparamref name="TOptions"/> are validated at startup.
    /// </summary>
    /// <typeparam name="TOptions">The backend's options, named <see cref="Name"/>.</typeparam>
    /// <param name="provider">A short, lower-case provider name reported as <c>lockingkit.provider</c>, such as <c>azure_blob</c>.</param>
    /// <param name="factory">Creates the backend from its options. Called once, on first resolution.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">A provider called <see cref="Name"/> is already registered.</exception>
    public LockingBuilder UseBackend<TOptions>(string provider, Func<IServiceProvider, TOptions, ILockBackend> factory)
        where TOptions : LockingOptions
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(factory);

        if (Services.Any(d => d.ServiceType == typeof(IDistributedLock) && d.IsKeyedService && Equals(d.ServiceKey, Name)))
        {
            throw new InvalidOperationException($"A lock provider called '{Name}' is already registered.");
        }

        var name = Name;
        var path = DistributedLocks.SectionPath(name);

        Services.AddOptions<TOptions>(name)
            .Validate(o => o.ValidateEngineSettings() is null, $"{path}: MinRetryDelay and ReleaseTimeout must be positive, and MaxRetryDelay at least MinRetryDelay.")
            .ValidateOnStart();

        Services.AddMetrics();
        Services.TryAddSingleton<LockingKitMetrics>();
        Services.TryAddSingleton(TimeProvider.System);

        Services.AddKeyedSingleton<IDistributedLock>(name, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name);

            return new DistributedLock(
                name,
                provider,
                factory(sp, options),
                options,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<LockingKitMetrics>());
        });

        if (name == DistributedLocks.DefaultName)
        {
            Services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IDistributedLock>(DistributedLocks.DefaultName));
        }

        return this;
    }
}

/// <summary>Registers the in-memory provider.</summary>
public static class InMemoryLockingBuilderExtensions
{
    /// <summary>The provider name reported as <c>lockingkit.provider</c>.</summary>
    public const string ProviderName = "in_memory";

    /// <summary>
    /// Backs the provider with in-memory leases: mutual exclusion within this process, with the expiry, renewal and
    /// fencing behaviour of a real lease backend. For tests and single-process hosts.
    /// </summary>
    /// <param name="builder">The builder from <see cref="LockingServiceCollectionExtensions.AddLocking"/>.</param>
    /// <param name="configure">Sets <see cref="InMemoryLockOptions"/>.</param>
    /// <returns>The builder.</returns>
    public static LockingBuilder UseInMemory(this LockingBuilder builder, Action<InMemoryLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Services.AddOptions<InMemoryLockOptions>(builder.Name);

        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(
            o => o.LeaseDuration >= TimeSpan.FromMilliseconds(30),
            $"{DistributedLocks.SectionPath(builder.Name)}:LeaseDuration must be at least 30 ms.");

        return builder.UseBackend<InMemoryLockOptions>(
            ProviderName,
            (sp, o) => new InMemoryLockBackend(o.LeaseDuration, sp.GetRequiredService<TimeProvider>()));
    }
}
