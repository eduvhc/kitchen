using Microsoft.Extensions.DependencyInjection;

namespace StoringKit.UnitTests;

[TestClass]
public sealed class RegistrationTests
{
    [TestMethod]
    public void The_default_store_resolves_keyed_and_unkeyed_as_the_same_instrumented_singleton()
    {
        var services = new ServiceCollection();
        services.AddObjectStore(ObjectStores.DefaultName, "fake", _ => new FakeObjectStore());
        using var provider = services.BuildServiceProvider();

        var unkeyed = provider.GetRequiredService<IObjectStore>();
        var keyed = provider.GetRequiredKeyedService<IObjectStore>(ObjectStores.DefaultName);

        Assert.AreSame(unkeyed, keyed);
        var instrumented = Assert.IsInstanceOfType<InstrumentedObjectStore>(unkeyed);
        Assert.AreEqual("Default", instrumented.Store);
        Assert.AreEqual("fake", instrumented.Provider);
        Assert.IsInstanceOfType<FakeObjectStore>(instrumented.Inner);
    }

    [TestMethod]
    public void Named_stores_are_keyed_and_do_not_claim_the_unkeyed_slot()
    {
        var services = new ServiceCollection();
        services.AddObjectStore("Public", "fake", _ => new FakeObjectStore(new Uri("https://cdn.example.com/")));
        services.AddObjectStore("Private", "fake", _ => new FakeObjectStore());
        using var provider = services.BuildServiceProvider();

        Assert.IsNull(provider.GetService<IObjectStore>());
        Assert.IsTrue(provider.GetRequiredKeyedService<IObjectStore>("Public").GetPublicUrl("a").IsSuccess);
        Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetRequiredKeyedService<IObjectStore>("Private").GetPublicUrl("a"));
    }

    [TestMethod]
    public void Registering_two_stores_under_one_name_fails_at_registration()
    {
        var services = new ServiceCollection();
        services.AddObjectStore("Files", "fake", _ => new FakeObjectStore());

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => services.AddObjectStore("Files", "other", _ => new FakeObjectStore()));
        StringAssert.Contains(ex.Message, "'Files'");
    }

    [TestMethod]
    public void Disposing_the_container_disposes_the_provider()
    {
        var fake = new FakeObjectStore();
        var services = new ServiceCollection();
        services.AddObjectStore(ObjectStores.DefaultName, "fake", _ => fake);

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<IObjectStore>();
        }

        Assert.IsTrue(fake.Disposed);
    }

    [TestMethod]
    public void Section_paths_nest_under_storing()
    {
        Assert.AreEqual("Storing:Default", ObjectStores.SectionPath(ObjectStores.DefaultName));
        Assert.AreEqual("Storing:Avatars", ObjectStores.SectionPath("Avatars"));
    }
}
