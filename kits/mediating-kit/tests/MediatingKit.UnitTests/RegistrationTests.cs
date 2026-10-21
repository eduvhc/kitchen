
namespace MediatingKit.UnitTests;

[TestClass]
public sealed class RegistrationTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task Scoped_handlers_are_shared_within_a_scope_and_fresh_in_the_next()
    {
        await using var host = TestHost.Create();

        Guid first, again, other;

        using (var scope = host.Root.CreateScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new WhoHandles(), Ct);
            again = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new WhoHandles(), Ct);
        }

        using (var scope = host.Root.CreateScope())
        {
            other = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new WhoHandles(), Ct);
        }

        Assert.AreEqual(first, again);
        Assert.AreNotEqual(first, other);
    }

    [TestMethod]
    public async Task Singleton_handlers_are_shared_across_scopes()
    {
        await using var host = TestHost.Create(o => o.Lifetime = ServiceLifetime.Singleton);

        var first = await host.Root.GetRequiredService<IMediator>().Send(new WhoHandles(), Ct);

        using var scope = host.Root.CreateScope();
        var second = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new WhoHandles(), Ct);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task Transient_mediators_get_their_own_handlers()
    {
        await using var host = TestHost.Create(o => o.Lifetime = ServiceLifetime.Transient);
        using var scope = host.Root.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var first = await mediator.Send(new WhoHandles(), Ct);
        var same = await mediator.Send(new WhoHandles(), Ct);
        var other = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new WhoHandles(), Ct);

        Assert.AreEqual(first, same, "one mediator instance keeps its handlers");
        Assert.AreNotEqual(first, other);
    }

    [TestMethod]
    public void Every_handler_in_the_project_is_registered_with_the_configured_lifetime()
    {
        var services = new ServiceCollection();
        services.AddMediator(o => o.Lifetime = ServiceLifetime.Singleton);

        foreach (var handler in new[] { typeof(DeleteFileHandler), typeof(TouchHandler), typeof(CountToHandler), typeof(AuditFileDeleted), typeof(CountEverything) })
        {
            var descriptor = services.Single(d => d.ServiceType == handler);
            Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime, handler.Name);
        }

        Assert.AreEqual(ServiceLifetime.Singleton, services.Single(d => d.ServiceType == typeof(IMediator)).Lifetime);
    }

    [TestMethod]
    public void A_handler_registered_by_hand_is_left_alone()
    {
        var services = new ServiceCollection();
        services.AddSingleton<PingHandler>();
        services.AddMediator();

        Assert.AreEqual(ServiceLifetime.Singleton, services.Single(d => d.ServiceType == typeof(PingHandler)).Lifetime);
    }

    [TestMethod]
    public void Calling_AddMediator_twice_from_one_project_registers_once()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        services.AddMediator();

        Assert.ContainsSingle(services.Where(d => d.ServiceType == typeof(IMediator)));
        Assert.ContainsSingle(services.Where(d => d.ServiceType == typeof(DeleteFileHandler)));
    }

    [TestMethod]
    public void A_second_project_calling_AddMediator_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddMediator();

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => MediatorRegistration.Register(
            services,
            configure: null,
            typeof(string),
            _ => throw new NotSupportedException(),
            (_, _) => { },
            requestTypes: 0));

        StringAssert.Contains(error.Message, "MediatingKit.UnitTests");
        StringAssert.Contains(error.Message, "System.Private.CoreLib");
    }

    [TestMethod]
    public void An_AddMediator_the_generator_did_not_replace_explains_why()
    {
        // A method group is not a call site, so nothing intercepts it: this is the body that runs without the generator.
        Func<IServiceCollection, Action<MediatorOptions>?, IServiceCollection> unintercepted = MediatingServiceCollectionExtensions.AddMediator;

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => unintercepted(new ServiceCollection(), null));

        StringAssert.Contains(error.Message, "source generator");
        StringAssert.Contains(error.Message, "InterceptorsNamespaces");
    }

    [TestMethod]
    public void An_undefined_lifetime_is_rejected()
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddMediator(o => o.Lifetime = (ServiceLifetime)42));
    }
}
