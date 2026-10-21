namespace MediatingKit;

/// <summary>Container-wide state every mediator instance shares: telemetry and the publishing strategy.</summary>
internal sealed class MediatorRuntime(MediatorTelemetry telemetry, NotificationPublishing notificationPublishing)
{
    public MediatorTelemetry Telemetry { get; } = telemetry;

    public NotificationPublishing NotificationPublishing { get; } = notificationPublishing;
}

/// <summary>Records which generated mediator a container was given, so a second <c>AddMediator()</c> is caught.</summary>
internal sealed class MediatorRegistrationMarker(Type mediatorType)
{
    public Type MediatorType { get; } = mediatorType;
}
