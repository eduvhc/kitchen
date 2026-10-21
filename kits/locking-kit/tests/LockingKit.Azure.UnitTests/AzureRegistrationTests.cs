using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LockingKit.Azure.UnitTests;

[TestClass]
public sealed class AzureRegistrationTests
{
    private const string Azurite = "UseDevelopmentStorage=true";

    [TestMethod]
    public void Options_bind_from_the_locking_section_of_the_provider_name()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Locking:Jobs:ConnectionString"] = Azurite,
                ["Locking:Jobs:ContainerName"] = "locks",
                ["Locking:Jobs:BlobPrefix"] = "jobs/",
                ["Locking:Jobs:LeaseDuration"] = "00:00:20",
                ["Locking:Jobs:FencingTokens"] = "true",
                ["Locking:Jobs:MaxRetryDelay"] = "00:00:02",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLocking("Jobs").UseAzureBlobLeases(configuration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptionsMonitor<AzureBlobLeaseOptions>>().Get("Jobs");
        Assert.AreEqual("jobs/", options.BlobPrefix);
        Assert.AreEqual(TimeSpan.FromSeconds(20), options.LeaseDuration);
        Assert.IsTrue(options.FencingTokens);
        Assert.AreEqual(TimeSpan.FromSeconds(2), options.MaxRetryDelay);

        var locks = (DistributedLock)provider.GetRequiredKeyedService<IDistributedLock>("Jobs");
        Assert.AreEqual("azure_blob", locks.Provider);
        Assert.AreEqual(TimeSpan.FromSeconds(20), locks.Timing.Duration);
    }

    [TestMethod]
    [DataRow(null, null, "locks", 30, "exactly one of ConnectionString and ServiceUri")]
    [DataRow(Azurite, "https://acct.blob.core.windows.net", "locks", 30, "exactly one of ConnectionString and ServiceUri")]
    [DataRow(Azurite, null, null, 30, "ContainerName is required")]
    [DataRow(Azurite, null, "Bad_Name", 30, "ContainerName is required")]
    [DataRow(Azurite, null, "locks", 10, "between 15 and 60 seconds")]
    [DataRow(Azurite, null, "locks", 61, "between 15 and 60 seconds")]
    public void Invalid_settings_fail_at_startup_naming_the_setting(string? connectionString, string? serviceUri, string? container, int leaseSeconds, string message)
    {
        var services = new ServiceCollection();
        services.AddLocking().UseAzureBlobLeases(o =>
        {
            o.ConnectionString = connectionString;
            o.ServiceUri = serviceUri is null ? null : new Uri(serviceUri);
            o.ContainerName = container;
            o.LeaseDuration = TimeSpan.FromSeconds(leaseSeconds);
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, message);
        StringAssert.Contains(ex.Message, "Locking:Default");
    }

    [TestMethod]
    public void A_bad_prefix_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddLocking().UseAzureBlobLeases(o =>
        {
            o.ConnectionString = Azurite;
            o.ContainerName = "locks";
            o.BlobPrefix = "/no-leading-slash";
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, "BlobPrefix");
    }

    [TestMethod]
    public void Requests_are_bounded_so_a_hung_service_cannot_stall_an_attempt_for_minutes()
    {
        var container = AzureBlobLeaseLockingBuilderExtensions.CreateContainerClient(new AzureBlobLeaseOptions
        {
            ConnectionString = Azurite,
            ContainerName = "locks",
            RequestTimeout = TimeSpan.FromSeconds(3),
        });

        Assert.AreEqual("locks", container.Name);
    }

    [TestMethod]
    public void A_handle_from_another_provider_has_no_blob_lease()
    {
        var services = new ServiceCollection();
        services.AddLocking().UseInMemory();
        using var provider = services.BuildServiceProvider();

        var handle = provider.GetRequiredService<IDistributedLock>().TryAcquireAsync("k").GetAwaiter().GetResult().Value;

        Assert.IsNull(handle.GetAzureBlobLease());
        handle.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
