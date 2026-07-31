using Microsoft.Extensions.DependencyInjection;

namespace TestingKit.Tests;

[TestClass]
public class TestEnvironmentTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Starts_every_registered_fixture_once()
    {
        var fixture = new CountingFixture();
        await using var environment = new TestEnvironment();
        environment.AddFixture(fixture);

        await environment.StartAsync(TestContext.CancellationToken);
        await environment.StartAsync(TestContext.CancellationToken);

        Assert.AreEqual(1, fixture.StartCount);
    }

    [TestMethod]
    public async Task Resets_only_resettable_fixtures()
    {
        var resettable = new CountingFixture();
        var plain = new PlainFixture();
        await using var environment = new TestEnvironment();
        environment.AddFixture(resettable);
        environment.AddFixture(plain);

        await environment.StartAsync(TestContext.CancellationToken);
        await environment.ResetAsync(TestContext.CancellationToken);
        await environment.ResetAsync(TestContext.CancellationToken);

        Assert.AreEqual(2, resettable.ResetCount);
    }

    [TestMethod]
    public async Task Evaluates_settings_after_fixtures_start()
    {
        var fixture = new CountingFixture();
        await using var environment = new TestEnvironment();
        environment.AddFixture(fixture);
        environment.AddSetting("ConnectionStrings:Thing", () => fixture.ConnectionString);

        await environment.StartAsync(TestContext.CancellationToken);

        Assert.AreEqual("started", environment.Configuration["ConnectionStrings:Thing"]);
    }

    [TestMethod]
    public async Task Builds_a_service_provider_from_configuration()
    {
        await using var environment = new TestEnvironment();
        environment.AddSetting("Greeting", () => "hello");
        environment.ConfigureServices((configuration, services) =>
            services.AddSingleton(new Greeter(configuration["Greeting"]!)));

        await environment.StartAsync(TestContext.CancellationToken);

        Assert.AreEqual("hello", environment.GetRequiredService<Greeter>().Value);
    }

    [TestMethod]
    public async Task Throws_when_services_were_never_configured()
    {
        await using var environment = new TestEnvironment();
        await environment.StartAsync(TestContext.CancellationToken);

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = environment.ServiceProvider);
    }

    [TestMethod]
    public async Task Disposes_every_fixture()
    {
        var fixture = new CountingFixture();
        var environment = new TestEnvironment();
        environment.AddFixture(fixture);
        await environment.StartAsync(TestContext.CancellationToken);

        await environment.DisposeAsync();

        Assert.AreEqual(1, fixture.DisposeCount);
    }

    private sealed record Greeter(string Value);

    private sealed class PlainFixture : ITestFixture
    {
        public string ConnectionString { get; private set; } = null!;

        public bool IsRunning { get; private set; }

        public bool IsExternal => false;

        public Task StartAsync(CancellationToken ct = default)
        {
            ConnectionString = "started";
            IsRunning = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingFixture : ITestFixture, IResettableFixture
    {
        public int StartCount { get; private set; }

        public int ResetCount { get; private set; }

        public int DisposeCount { get; private set; }

        public string ConnectionString { get; private set; } = null!;

        public bool IsRunning { get; private set; }

        public bool IsExternal => false;

        public Task StartAsync(CancellationToken ct = default)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            StartCount++;
            ConnectionString = "started";
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken ct = default)
        {
            ResetCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
