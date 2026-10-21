using Microsoft.Extensions.DependencyInjection;

namespace SourceGeneratedMediator;

/// <summary>
/// Mediator's registration, outside the MediatingKit namespace so <c>AddMediator</c> binds to Mediator's generated
/// extension rather than MediatingKit's.
/// </summary>
internal static class SourceGeneratedMediatorSetup
{
    public static IServiceCollection Add(IServiceCollection services) =>
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Singleton);
}
