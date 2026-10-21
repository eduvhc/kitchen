using System.Diagnostics.Metrics;
using LockingKit.Azure;
using LockingKit.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace LockingKit.IntegrationTests;

internal sealed class TestOptions : LockingOptions;

/// <summary>
/// A backend under test and a way to start new "processes" against it: each is an engine with its own client,
/// connection pool and waiting room, sharing only the backend itself — exactly what separate hosts share.
/// </summary>
internal abstract class ProviderUnderTest : IAsyncDisposable
{
    private static readonly IMeterFactory Meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    private readonly List<DistributedLock> _processes = [];

    public abstract string Name { get; }

    /// <summary>Whether a lock expires when its holder stops renewing (a lease), or lives as long as its connection.</summary>
    public abstract bool Expires { get; }

    /// <summary>The lease duration of an expiring backend, or the health check interval of a connection-held one.</summary>
    public abstract TimeSpan Period { get; }

    public static TestOptions EngineOptions() => new()
    {
        MinRetryDelay = TimeSpan.FromMilliseconds(20),
        MaxRetryDelay = TimeSpan.FromMilliseconds(250),
        ReleaseTimeout = TimeSpan.FromSeconds(2),
    };

    public IDistributedLock NewProcess(Func<ILockBackend, ILockBackend>? wrap = null)
    {
        var backend = CreateBackend();
        var engine = new DistributedLock(
            "Default",
            Name,
            wrap is null ? backend : wrap(backend),
            EngineOptions(),
            TimeProvider.System,
            new LockingKitMetrics(Meters));

        lock (_processes)
        {
            _processes.Add(engine);
        }

        return engine;
    }

    public async Task StopProcessesAsync()
    {
        DistributedLock[] processes;

        lock (_processes)
        {
            processes = [.. _processes];
            _processes.Clear();
        }

        foreach (var process in processes)
        {
            await process.DisposeAsync();
        }
    }

    public virtual ValueTask DisposeAsync() => new(StopProcessesAsync());

    protected abstract ILockBackend CreateBackend();
}

internal sealed class InMemoryUnderTest : ProviderUnderTest
{
    // One table shared by every "process", as one backend would be.
    private readonly InMemoryLockBackend _backend = new(TimeSpan.FromSeconds(3), TimeProvider.System);

    public override string Name => InMemoryLockingBuilderExtensions.ProviderName;

    public override bool Expires => true;

    public override TimeSpan Period => TimeSpan.FromSeconds(3);

    protected override ILockBackend CreateBackend() => _backend;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _backend.DisposeAsync();
    }
}

internal sealed class AzureUnderTest(Func<string> connectionString, string container = TestHost.Container) : ProviderUnderTest
{
    public override string Name => AzureBlobLeaseLockingBuilderExtensions.ProviderName;

    public override bool Expires => true;

    // The shortest lease Azure grants.
    public override TimeSpan Period => TimeSpan.FromSeconds(15);

    protected override ILockBackend CreateBackend() =>
        new AzureBlobLeaseBackend(
            AzureBlobLeaseLockingBuilderExtensions.CreateContainerClient(new AzureBlobLeaseOptions
            {
                ConnectionString = connectionString(),
                ContainerName = container,
                RequestTimeout = TimeSpan.FromSeconds(3),
            }),
            "locks/",
            Period,
            fencingTokens: true);
}

internal sealed class PostgresUnderTest(Func<string> connectionString) : ProviderUnderTest
{
    public override string Name => PostgresLockingBuilderExtensions.ProviderName;

    public override bool Expires => false;

    public override TimeSpan Period => TimeSpan.FromMilliseconds(500);

    protected override ILockBackend CreateBackend() =>
        PostgresLockingBuilderExtensions.CreateBackend(new PostgresLockOptions
        {
            ConnectionString = connectionString(),
            HealthCheckInterval = Period,
            FencingTokens = true,
        });
}
