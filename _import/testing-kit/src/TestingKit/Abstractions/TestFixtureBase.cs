namespace TestingKit;

public abstract class TestFixtureBase<TContainerOptions, TClientOptions>(
    TContainerOptions? containerOptions = null,
    TClientOptions? clientOptions = null) : ITestFixture
    where TContainerOptions : ContainerOptions, new()
    where TClientOptions : ClientOptions, new()
{
    private int _started;

    protected TContainerOptions Container { get; } = containerOptions ?? new TContainerOptions();

    protected TClientOptions Client { get; } = clientOptions ?? new TClientOptions();

    public string ConnectionString { get; protected set; } = null!;

    public bool IsRunning { get; protected set; }

    public bool IsExternal => Client.ConnectionString is not null;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        await OnBeforeStartAsync(ct);

        if (IsExternal)
        {
            ConnectionString = Client.ConnectionString!;
            await OnExternalConnectedAsync(ct);
        }
        else
        {
            await StartContainerAsync(ct);
            IsRunning = true;
        }

        await OnAfterStartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        await OnBeforeDisposeAsync();

        if (IsRunning)
        {
            await DisposeContainerAsync();
            IsRunning = false;
        }

        GC.SuppressFinalize(this);
    }

    protected abstract Task StartContainerAsync(CancellationToken ct);

    protected abstract ValueTask DisposeContainerAsync();

    protected virtual Task OnBeforeStartAsync(CancellationToken ct) => Task.CompletedTask;

    protected virtual Task OnAfterStartAsync(CancellationToken ct) => Task.CompletedTask;

    protected virtual Task OnExternalConnectedAsync(CancellationToken ct) => Task.CompletedTask;

    protected virtual Task OnBeforeDisposeAsync() => Task.CompletedTask;

    protected void EnsureReady()
    {
        if (ConnectionString is null)
        {
            throw new InvalidOperationException($"{GetType().Name} is not ready. Call StartAsync() first.");
        }
    }
}
