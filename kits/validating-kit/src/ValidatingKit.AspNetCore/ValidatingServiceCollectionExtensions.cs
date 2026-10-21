using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ValidatingKit.AspNetCore;

/// <summary>Registers validators so <c>WithValidation()</c> can find them.</summary>
/// <remarks>
/// Validators are registered as singletons: <see cref="Validator{T}"/> keeps no state. Each is registered once per
/// <see cref="IValidator{T}"/> it implements, and only if nothing is registered for that service yet, so an
/// application's own registration wins.
/// </remarks>
public static class ValidatingServiceCollectionExtensions
{
    /// <summary>Registers <typeparamref name="TValidator"/> for every <see cref="IValidator{T}"/> it implements.</summary>
    /// <typeparam name="TValidator">The validator, such as <c>OrderValidator</c>.</typeparam>
    /// <param name="services">The services.</param>
    /// <exception cref="ArgumentException"><typeparamref name="TValidator"/> implements no <see cref="IValidator{T}"/>.</exception>
    public static IServiceCollection AddValidator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TValidator>(
        this IServiceCollection services)
        where TValidator : class
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!Register(services, typeof(TValidator)))
        {
            throw new ArgumentException($"{typeof(TValidator).Name} implements no IValidator<T>.", nameof(TValidator));
        }

        return services;
    }

    /// <summary>Registers every concrete validator in <paramref name="assembly"/>.</summary>
    /// <param name="services">The services.</param>
    /// <param name="assembly">The assembly to scan, usually the application's own.</param>
    [RequiresUnreferencedCode("Scans the assembly's types. Register validators with AddValidator<T>() when trimming.")]
    public static IServiceCollection AddValidatorsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            if (type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            {
                Register(services, type);
            }
        }

        return services;
    }

    /// <summary>Registers every concrete validator in the assembly that defines <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">Any type in the assembly to scan, such as <c>Program</c>.</typeparam>
    /// <param name="services">The services.</param>
    /// <example><c>builder.Services.AddValidatorsFromAssemblyContaining&lt;Program&gt;();</c></example>
    [RequiresUnreferencedCode("Scans the assembly's types. Register validators with AddValidator<T>() when trimming.")]
    public static IServiceCollection AddValidatorsFromAssemblyContaining<T>(this IServiceCollection services) =>
        services.AddValidatorsFromAssembly(typeof(T).Assembly);

    private static bool Register(
        IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        var registered = false;

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidator<>))
            {
                if (!registered)
                {
                    services.TryAddSingleton(type);
                }

                services.TryAdd(ServiceDescriptor.Singleton(contract, provider => provider.GetRequiredService(type)));
                registered = true;
            }
        }

        return registered;
    }
}
