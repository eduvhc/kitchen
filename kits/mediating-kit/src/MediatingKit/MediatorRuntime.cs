namespace MediatingKit;

/// <summary>Container-wide state every mediator instance shares: telemetry, the publishing strategy, and what is known about behaviors.</summary>
internal sealed class MediatorRuntime(MediatorTelemetry telemetry, NotificationPublishing notificationPublishing, int requestTypes)
{
    // One flag per request type the generated mediator dispatches: set once a container is known to have no behavior
    // for it. Which behaviors apply is fixed by the registrations, so later scopes skip resolving them altogether.
    private readonly bool[] _behaviorless = new bool[requestTypes];

    public MediatorTelemetry Telemetry { get; } = telemetry;

    public NotificationPublishing NotificationPublishing { get; } = notificationPublishing;

    public bool IsBehaviorless(int request) => Volatile.Read(ref _behaviorless[request]);

    public void MarkBehaviorless(int request) => Volatile.Write(ref _behaviorless[request], true);
}

/// <summary>Records which generated mediator a container was given, so a second <c>AddMediator()</c> is caught.</summary>
internal sealed class MediatorRegistrationMarker(Type mediatorType)
{
    public Type MediatorType { get; } = mediatorType;
}
