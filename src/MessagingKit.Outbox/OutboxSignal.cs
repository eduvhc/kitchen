namespace MessagingKit.Outbox;

/// <summary>Pulsed when a transaction commits new outbox rows.</summary>
public sealed class OutboxSignal : WorkSignal;
