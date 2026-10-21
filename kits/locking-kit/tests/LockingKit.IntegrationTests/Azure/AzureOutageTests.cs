using System.Diagnostics;
using Azure.Storage.Blobs;
using Testcontainers.Azurite;
using TestingKit.Azurite;

namespace LockingKit.IntegrationTests;

/// <summary>Azure becomes unreachable while a lock is held: a dedicated Azurite, paused mid-hold.</summary>
[TestClass]
public sealed class AzureOutageTests
{
    private static AzuriteContainer _azurite = null!;
    private static AzureUnderTest _provider = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context)
    {
        _azurite = new AzuriteBuilder(AzuriteContainerOptions.DefaultImage).WithCommand("--skipApiVersionCheck").Build();
        await _azurite.StartAsync(context.CancellationToken);

        var connectionString = _azurite.GetConnectionString();
        await new BlobContainerClient(connectionString, TestHost.Container)
            .CreateIfNotExistsAsync(cancellationToken: context.CancellationToken);

        _provider = new AzureUnderTest(() => connectionString);
    }

    [ClassCleanup]
    public static async Task StopAsync()
    {
        await _provider.StopProcessesAsync();
        await _azurite.DisposeAsync();
    }

    [TestMethod]
    public async Task Renewals_failing_against_an_unreachable_service_fire_LostToken_before_the_lease_expires()
    {
        var a = _provider.NewProcess();
        var key = $"outage/{Guid.NewGuid():N}";
        var clock = Stopwatch.StartNew();
        var handle = (await a.TryAcquireAsync(key, Ct)).Value;
        var lostAt = TimeSpan.MaxValue;
        handle.LostToken.Register(() => lostAt = clock.Elapsed);

        await _azurite.PauseAsync(Ct);

        try
        {
            while (!handle.IsLost)
            {
                Assert.IsLessThan(_provider.Period + TimeSpan.FromSeconds(2), clock.Elapsed, "Still claiming the lock past its lease.");
                await Task.Delay(100, Ct);
            }
        }
        finally
        {
            await _azurite.UnpauseAsync(Ct);
        }

        Assert.AreEqual(LockLossReason.Expired, handle.LossReason);

        // Given up a safety margin before Azure's own expiry (15 s after the acquire was sent, at the latest).
        Assert.IsGreaterThanOrEqualTo(_provider.Period * 0.85, lostAt);
        Assert.IsLessThan(_provider.Period, lostAt);

        // Once the service is back, the lease has expired and someone else can take the key.
        var b = _provider.NewProcess();
        var taken = await b.AcquireAsync(key, TimeSpan.FromSeconds(20), Ct);
        Assert.IsTrue(taken.IsSuccess, taken.ToString());
        Assert.IsLessThan(clock.Elapsed, lostAt);

        await handle.DisposeAsync();
        await taken.Value.DisposeAsync();
    }

    [TestMethod]
    public async Task Attempts_against_an_unreachable_service_are_transient_results_within_the_request_timeout()
    {
        var a = _provider.NewProcess();
        var key = $"outage/{Guid.NewGuid():N}";

        await _azurite.PauseAsync(Ct);

        try
        {
            var watch = Stopwatch.StartNew();
            var result = await a.TryAcquireAsync(key, Ct);

            Assert.AreEqual(LockingErrors.Unavailable, result.Error?.Code, result.ToString());
            Assert.IsTrue(result.Error!.IsTransient);
            Assert.IsLessThan(TimeSpan.FromSeconds(20), watch.Elapsed, "Three bounded requests, not the SDK's 100 s default.");
        }
        finally
        {
            await _azurite.UnpauseAsync(Ct);
        }

        await using var handle = (await a.AcquireAsync(key, TimeSpan.FromSeconds(10), Ct)).Value;
    }
}
