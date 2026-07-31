using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TestingKit;

public sealed class TestEnvironment : IAsyncDisposable
{
    private readonly List<ITestFixture> _fixtures = [];
    private readonly List<Action<IConfigurationBuilder>> _configurationCallbacks = [];
    private readonly List<Action<IConfiguration, IServiceCollection>> _serviceCallbacks = [];
    private readonly Dictionary<string, Func<string?>> _settings = [];

    private ServiceProvider? _serviceProvider;
    private bool _started;

    public IConfiguration Configuration { get; private set; } = null!;

    public IServiceProvider ServiceProvider => _serviceProvider
        ?? throw new InvalidOperationException("No services were registered. Call ConfigureServices() before StartAsync().");

    public IReadOnlyList<ITestFixture> Fixtures => _fixtures;

    public TFixture AddFixture<TFixture>(TFixture fixture)
        where TFixture : ITestFixture
    {
        _fixtures.Add(fixture);
        return fixture;
    }

    public TestEnvironment AddSetting(string key, Func<string?> valueFactory)
    {
        _settings[key] = valueFactory;
        return this;
    }

    public TestEnvironment ConfigureConfiguration(Action<IConfigurationBuilder> configure)
    {
        _configurationCallbacks.Add(configure);
        return this;
    }

    public TestEnvironment ConfigureServices(Action<IServiceCollection> configure)
    {
        _serviceCallbacks.Add((_, services) => configure(services));
        return this;
    }

    public TestEnvironment ConfigureServices(Action<IConfiguration, IServiceCollection> configure)
    {
        _serviceCallbacks.Add(configure);
        return this;
    }

    public IReadOnlyDictionary<string, string?> BuildSettings() =>
        _settings.ToDictionary(pair => pair.Key, pair => pair.Value());

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        await Task.WhenAll(_fixtures.Select(fixture => fixture.StartAsync(ct)));

        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(BuildSettings());

        foreach (var callback in _configurationCallbacks)
        {
            callback(configurationBuilder);
        }

        Configuration = configurationBuilder.Build();

        if (_serviceCallbacks.Count == 0)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddSingleton(Configuration);

        foreach (var callback in _serviceCallbacks)
        {
            callback(Configuration, services);
        }

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        foreach (var fixture in _fixtures.OfType<IResettableFixture>())
        {
            await fixture.ResetAsync(ct);
        }
    }

    public AsyncServiceScope CreateAsyncScope() => ServiceProvider.CreateAsyncScope();

    public T GetRequiredService<T>()
        where T : notnull => ServiceProvider.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
            _serviceProvider = null;
        }

        await Task.WhenAll(_fixtures.Select(fixture => fixture.DisposeAsync().AsTask()));

        _fixtures.Clear();
        _started = false;
    }
}
