namespace TestingKit;

public interface ITestFixture : IAsyncDisposable
{
    string ConnectionString { get; }

    bool IsRunning { get; }

    bool IsExternal { get; }

    Task StartAsync(CancellationToken ct = default);
}

public interface IResettableFixture
{
    Task ResetAsync(CancellationToken ct = default);
}
