using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LockingKit.UnitTests;

[TestClass]
public sealed class RegistrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void The_default_provider_resolves_keyed_and_unkeyed_as_the_same_singleton()
    {
        var services = new ServiceCollection();
        services.AddLocking().UseInMemory();
        using var provider = services.BuildServiceProvider();

        var unkeyed = provider.GetRequiredService<IDistributedLock>();
        var keyed = provider.GetRequiredKeyedService<IDistributedLock>(DistributedLocks.DefaultName);

        Assert.AreSame(unkeyed, keyed);
        var engine = Assert.IsInstanceOfType<DistributedLock>(unkeyed);
        Assert.AreEqual("Default", engine.Name);
        Assert.AreEqual("in_memory", engine.Provider);
    }

    [TestMethod]
    public async Task Named_providers_are_keyed_independent_and_do_not_claim_the_unkeyed_slot()
    {
        var services = new ServiceCollection();
        services.AddLocking("Jobs").UseInMemory();
        services.AddLocking("Tenants").UseInMemory();
        await using var provider = services.BuildServiceProvider();

        Assert.IsNull(provider.GetService<IDistributedLock>());

        var jobs = provider.GetRequiredKeyedService<IDistributedLock>("Jobs");
        var tenants = provider.GetRequiredKeyedService<IDistributedLock>("Tenants");
        await using var held = await jobs.HoldAsync("k");

        // Separate backends: the same key in another provider is a different lock.
        await using var other = await tenants.HoldAsync("k");
    }

    [TestMethod]
    public void Registering_two_backends_under_one_name_fails_at_registration()
    {
        var services = new ServiceCollection();
        services.AddLocking("Jobs").UseInMemory();

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => services.AddLocking("Jobs").UseInMemory());
        StringAssert.Contains(ex.Message, "'Jobs'");
    }

    [TestMethod]
    public void Engine_settings_are_validated_at_startup()
    {
        var services = new ServiceCollection();
        services.AddLocking().UseInMemory(o =>
        {
            o.MinRetryDelay = TimeSpan.FromSeconds(2);
            o.MaxRetryDelay = TimeSpan.FromSeconds(1);
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, "Locking:Default");
    }

    [TestMethod]
    public void The_in_memory_lease_duration_is_validated_at_startup()
    {
        var services = new ServiceCollection();
        services.AddLocking().UseInMemory(o => o.LeaseDuration = TimeSpan.FromMilliseconds(5));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, "LeaseDuration");
    }

    [TestMethod]
    public async Task A_custom_backend_plugs_into_the_same_engine()
    {
        var backend = ScriptedBackend.Expiring(TimeSpan.FromSeconds(30));
        var services = new ServiceCollection();
        services.AddLocking("Custom").UseBackend<TestLockingOptions>("scripted", (_, _) => backend);
        await using var provider = services.BuildServiceProvider();

        await using var handle = await provider.GetRequiredKeyedService<IDistributedLock>("Custom").HoldAsync("k");

        Assert.AreEqual(1, backend.Attempts);
        Assert.AreEqual(1L, handle.FencingToken);
    }

    [TestMethod]
    public async Task The_host_clock_drives_every_lease()
    {
        var time = Engines.Clock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddLocking().UseInMemory(o => o.LeaseDuration = TimeSpan.FromSeconds(30));
        await using var provider = services.BuildServiceProvider();
        var locks = provider.GetRequiredService<IDistributedLock>();

        Assert.AreSame(time, ((DistributedLock)locks).TimeProvider);
        await using var handle = await locks.HoldAsync();
    }

    [TestMethod]
    public async Task Disposing_the_container_releases_held_locks_and_the_backend()
    {
        var backend = ScriptedBackend.Expiring(TimeSpan.FromSeconds(30));
        var services = new ServiceCollection();
        services.AddLocking().UseBackend<TestLockingOptions>("scripted", (_, _) => backend);

        ILockHandle handle;

        await using (var provider = services.BuildServiceProvider())
        {
            handle = await provider.GetRequiredService<IDistributedLock>().HoldAsync();
        }

        Assert.AreEqual(1, backend.Releases);
        Assert.IsTrue(backend.Disposed);
        Assert.IsTrue(handle.LostToken.IsCancellationRequested);
    }

    [TestMethod]
    public void A_container_disposed_synchronously_releases_too()
    {
        var backend = ScriptedBackend.Expiring(TimeSpan.FromSeconds(30));
        var services = new ServiceCollection();
        services.AddLocking().UseBackend<TestLockingOptions>("scripted", (_, _) => backend);

        using (var provider = services.BuildServiceProvider())
        {
            _ = provider.GetRequiredService<IDistributedLock>().HoldAsync().GetAwaiter().GetResult();
        }

        Assert.AreEqual(1, backend.Releases);
    }

    [TestMethod]
    public void Section_paths_nest_under_locking()
    {
        Assert.AreEqual("Locking:Default", DistributedLocks.SectionPath(DistributedLocks.DefaultName));
        Assert.AreEqual("Locking:Jobs", DistributedLocks.SectionPath("Jobs"));
    }
}
