namespace LockingKit;

/// <summary>Settings for the in-memory provider registered by <c>UseInMemory()</c>.</summary>
/// <remarks>
/// The in-memory provider excludes only within one process (one registration), but otherwise behaves like a lease
/// backend: leases expire unless renewed, a handle that stops renewing loses its lock, and fencing tokens increase.
/// It is meant for tests and single-process hosts.
/// </remarks>
public sealed class InMemoryLockOptions : LockingOptions
{
    /// <summary>How long a lease lasts without renewal. At least 30 ms; defaults to 30 s.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
}
