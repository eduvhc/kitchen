using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TestingKit.AspNetCore;

public class TestingKitWebApplicationFactory<TEntryPoint>(TestEnvironment environment)
    : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly List<Action<IServiceCollection>> _serviceOverrides = [];

    public TestEnvironment Environment { get; } = environment;

    public string EnvironmentName { get; init; } = "Testing";

    public TestingKitWebApplicationFactory<TEntryPoint> WithServices(Action<IServiceCollection> configure)
    {
        _serviceOverrides.Add(configure);
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(EnvironmentName);

        foreach (var (key, value) in Environment.BuildSettings())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            foreach (var configure in _serviceOverrides)
            {
                configure(services);
            }
        });
    }
}
