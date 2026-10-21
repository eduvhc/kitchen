namespace MediatingKit.UnitTests;

/// <summary>A container with this project's generated mediator, validated the way a development host validates it.</summary>
internal sealed class TestHost : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;

    private TestHost(ServiceProvider root)
    {
        _root = root;
        _scope = root.CreateAsyncScope();
    }

    public IServiceProvider Root => _root;

    public IMediator Mediator => _scope.ServiceProvider.GetRequiredService<IMediator>();

    public Journal Journal => _root.GetRequiredService<Journal>();

    public static TestHost Create(Action<MediatorOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<Journal>();
        services?.Invoke(collection);
        collection.AddMediator(configure);

        return new TestHost(collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }
}
