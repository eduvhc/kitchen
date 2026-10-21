using System.Diagnostics;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using LockingKit.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LockingKit.IntegrationTests;

/// <summary>What only the Azure provider does: guard blobs, the lease id, fencing in metadata, external breaks.</summary>
[TestClass]
public sealed class AzureBlobLeaseTests
{
    private static readonly AzureUnderTest Provider = new(() => TestHost.Azurite.ConnectionString);

    private string _prefix = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    private static BlobContainerClient Container => TestHost.Azurite.BlobClient.GetBlobContainerClient(TestHost.Container);

    [TestInitialize]
    public void UseFreshKeys() => _prefix = $"azure/{Guid.NewGuid():N}/";

    [TestCleanup]
    public Task StopProcessesAsync() => Provider.StopProcessesAsync();

    [TestMethod]
    public async Task A_missing_guard_is_created_empty_under_the_prefix_with_a_readable_name()
    {
        var locks = Provider.NewProcess();
        var key = _prefix + "orders/42";

        await using (var handle = (await locks.TryAcquireAsync(key, Ct)).Value)
        {
            Assert.AreEqual($"locks/{key}", handle.GetAzureBlobLease()!.Blob.Name);
        }

        var properties = await Container.GetBlobClient($"locks/{key}").GetPropertiesAsync(cancellationToken: Ct);
        Assert.AreEqual(0, properties.Value.ContentLength);
        Assert.AreEqual(LeaseState.Available, properties.Value.LeaseState, "Released on dispose.");
    }

    [TestMethod]
    public async Task An_existing_guard_keeps_its_content_and_metadata_and_gains_only_the_fence()
    {
        var key = _prefix + "existing";
        var guard = Container.GetBlobClient($"locks/{key}");
        await guard.UploadAsync(BinaryData.FromString("owned by ops"), cancellationToken: Ct);
        await guard.SetMetadataAsync(new Dictionary<string, string> { ["owner"] = "ops" }, cancellationToken: Ct);

        var locks = Provider.NewProcess();

        for (var i = 1; i <= 3; i++)
        {
            await using var handle = (await locks.TryAcquireAsync(key, Ct)).Value;
            Assert.AreEqual(i, handle.FencingToken);
        }

        var content = await guard.DownloadContentAsync(Ct);
        Assert.AreEqual("owned by ops", content.Value.Content.ToString());
        var metadata = (await guard.GetPropertiesAsync(cancellationToken: Ct)).Value.Metadata;
        Assert.AreEqual("ops", metadata["owner"]);
        Assert.AreEqual("3", metadata[AzureBlobLeaseBackend.FenceMetadataName]);
        Assert.HasCount(2, metadata);
    }

    [TestMethod]
    public async Task The_lease_id_authorises_writes_to_the_guard_only_while_the_lock_is_held()
    {
        var locks = Provider.NewProcess();
        var handle = (await locks.TryAcquireAsync(_prefix + "guarded-state", Ct)).Value;
        var lease = handle.GetAzureBlobLease()!;

        await lease.Blob.UploadAsync(BinaryData.FromString("v1"), new BlobUploadOptions { Conditions = lease.Conditions }, Ct);

        var withoutLease = await Assert.ThrowsExactlyAsync<RequestFailedException>(
            () => lease.Blob.UploadAsync(BinaryData.FromString("intruder"), overwrite: true, Ct));
        Assert.AreEqual(412, withoutLease.Status);

        await handle.DisposeAsync();

        var stale = await Assert.ThrowsExactlyAsync<RequestFailedException>(
            () => lease.Blob.UploadAsync(BinaryData.FromString("stale"), new BlobUploadOptions { Conditions = lease.Conditions }, Ct));
        Assert.IsTrue(stale.Status is 409 or 412, $"{stale.Status} {stale.ErrorCode}");
        Assert.AreEqual("v1", (await lease.Blob.DownloadContentAsync(Ct)).Value.Content.ToString());
    }

    [TestMethod]
    public async Task Breaking_the_lease_from_outside_revokes_the_lock_at_the_next_renewal()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = _prefix + "broken";
        var handle = (await a.TryAcquireAsync(key, Ct)).Value;
        var watch = Stopwatch.StartNew();

        await handle.GetAzureBlobLease()!.Blob.GetBlobLeaseClient().BreakAsync(TimeSpan.Zero, cancellationToken: Ct);

        while (!handle.IsLost)
        {
            Assert.IsLessThan(Provider.Period / 3 + TimeSpan.FromSeconds(3), watch.Elapsed, "Not revoked by the next renewal.");
            await Task.Delay(50, Ct);
        }

        Assert.AreEqual(LockLossReason.Revoked, handle.LossReason);
        Assert.IsTrue(handle.LostToken.IsCancellationRequested);
        await using var next = (await b.TryAcquireAsync(key, Ct)).Value;
        await handle.DisposeAsync();
        Assert.AreEqual(LeaseState.Leased, (await next.GetAzureBlobLease()!.Blob.GetPropertiesAsync(cancellationToken: Ct)).Value.LeaseState, "The stale holder's release did not touch its successor's lease.");
    }

    [TestMethod]
    public async Task Deleting_the_guard_revokes_the_lock()
    {
        var locks = Provider.NewProcess();
        var handle = (await locks.TryAcquireAsync(_prefix + "deleted", Ct)).Value;
        var lease = handle.GetAzureBlobLease()!;

        await lease.Blob.DeleteAsync(conditions: lease.Conditions, cancellationToken: Ct);

        var watch = Stopwatch.StartNew();

        while (!handle.IsLost)
        {
            Assert.IsLessThan(Provider.Period / 3 + TimeSpan.FromSeconds(3), watch.Elapsed);
            await Task.Delay(50, Ct);
        }

        Assert.AreEqual(LockLossReason.Revoked, handle.LossReason);
        await handle.DisposeAsync();
    }

    [TestMethod]
    public async Task Registration_from_configuration_wires_a_working_lock()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Locking:Jobs:ConnectionString"] = TestHost.Azurite.ConnectionString,
                ["Locking:Jobs:ContainerName"] = TestHost.Container,
                ["Locking:Jobs:LeaseDuration"] = "00:00:15",
                ["Locking:Jobs:BlobPrefix"] = "jobs/",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLocking("Jobs").UseAzureBlobLeases(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();

        var locks = provider.GetRequiredKeyedService<IDistributedLock>("Jobs");
        await using var handle = (await locks.TryAcquireAsync(_prefix + "configured", Ct)).Value;

        Assert.AreEqual($"jobs/{_prefix}configured", handle.GetAzureBlobLease()!.Blob.Name);
        Assert.IsNull(handle.FencingToken, "Fencing is opt-in on Azure.");
        Assert.AreEqual(LockingErrors.Busy, (await locks.TryAcquireAsync(_prefix + "configured", Ct)).Error?.Code);
    }

    [TestMethod]
    public async Task A_container_that_does_not_exist_is_a_misconfiguration_result()
    {
        var services = new ServiceCollection();
        services.AddLocking().UseAzureBlobLeases(o =>
        {
            o.ConnectionString = TestHost.Azurite.ConnectionString;
            o.ContainerName = "no-such-container";
        });
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDistributedLock>().TryAcquireAsync(_prefix + "k", Ct);

        Assert.AreEqual(LockingErrors.Misconfigured, result.Error?.Code);
        StringAssert.Contains(result.Error!.Message, "ContainerNotFound");
    }
}
