using Microsoft.Extensions.DependencyInjection;

namespace MediatingKit;

/// <summary>Configures <c>services.AddMediator(o => ...)</c>.</summary>
public sealed class MediatorOptions
{
    private readonly List<Type> _behaviors = [];

    /// <summary>
    /// The lifetime of the mediator, its handlers and the behaviors added here. <see cref="ServiceLifetime.Scoped"/>
    /// by default, so handlers can take a <c>DbContext</c>.
    /// </summary>
    /// <remarks>
    /// The mediator resolves each handler and builds its pipeline once, on first use, and reuses them for its own
    /// lifetime: per scope when scoped, for the application when singleton. A transient mediator gets fresh handlers
    /// each time the mediator itself is resolved.
    /// </remarks>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>How a notification reaches its handlers. <see cref="MediatingKit.NotificationPublishing.Sequential"/> by default.</summary>
    public NotificationPublishing NotificationPublishing { get; set; } = NotificationPublishing.Sequential;

    /// <summary>The behaviors added so far, in the order they run: the first is outermost.</summary>
    public IReadOnlyList<Type> Behaviors => _behaviors;

    /// <summary>
    /// Adds a pipeline behavior. An open generic such as <c>typeof(LoggingBehavior&lt;,&gt;)</c> runs for every
    /// request its constraints allow; a closed type runs for the requests it implements
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/> for. Behaviors run in the order added, the first outermost.
    /// </summary>
    /// <param name="behaviorType">The behavior type.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="behaviorType"/> does not implement <see cref="IPipelineBehavior{TRequest, TResponse}"/>.</exception>
    public MediatorOptions AddBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (behaviorType.IsAbstract || behaviorType.IsInterface)
        {
            throw new ArgumentException($"'{behaviorType}' is abstract; a behavior must be a concrete class.", nameof(behaviorType));
        }

        if (behaviorType.IsGenericTypeDefinition && behaviorType.GetGenericArguments().Length != 2)
        {
            throw new ArgumentException(
                $"'{behaviorType}' is an open generic with {behaviorType.GetGenericArguments().Length} type parameters. "
                + "An open-generic behavior takes exactly <TRequest, TResponse>; narrow it with constraints, or register a closed type.",
                nameof(behaviorType));
        }

        if (!BehaviorInterfaces(behaviorType).Any())
        {
            throw new ArgumentException($"'{behaviorType}' does not implement IPipelineBehavior<TRequest, TResponse>.", nameof(behaviorType));
        }

        _behaviors.Add(behaviorType);
        return this;
    }

    /// <summary>Adds a closed pipeline behavior. See <see cref="AddBehavior(Type)"/>.</summary>
    /// <typeparam name="TBehavior">The behavior type.</typeparam>
    /// <returns>These options, for chaining.</returns>
    public MediatorOptions AddBehavior<TBehavior>()
        where TBehavior : class => AddBehavior(typeof(TBehavior));

    internal void RegisterBehaviors(IServiceCollection services)
    {
        foreach (var behavior in _behaviors)
        {
            if (behavior.IsGenericTypeDefinition)
            {
                services.Add(new ServiceDescriptor(typeof(IPipelineBehavior<,>), behavior, Lifetime));
                continue;
            }

            foreach (var service in BehaviorInterfaces(behavior))
            {
                services.Add(new ServiceDescriptor(service, behavior, Lifetime));
            }
        }
    }

    private static IEnumerable<Type> BehaviorInterfaces(Type type) =>
        type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
}

/// <summary>How a published notification reaches its handlers.</summary>
public enum NotificationPublishing
{
    /// <summary>
    /// One after another, each awaited before the next starts. The first exception stops the rest and reaches the
    /// publisher.
    /// </summary>
    Sequential,

    /// <summary>
    /// All at once, awaited together. Every handler runs even if one fails; one failure is rethrown as is, several as
    /// an <see cref="AggregateException"/>.
    /// </summary>
    Parallel,
}
