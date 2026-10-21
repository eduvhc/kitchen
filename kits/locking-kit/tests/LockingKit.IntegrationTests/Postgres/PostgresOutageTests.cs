using System.Diagnostics;
using Testcontainers.PostgreSql;
using TestingKit.Postgres;

namespace LockingKit.IntegrationTests;

/// <summary>The database stops answering while a lock is held: a dedicated PostgreSQL, paused mid-hold.</summary>
[TestClass]
public sealed class PostgresOutageTests
{
    private static PostgreSqlContainer _postgres = null!;
    private static PostgresUnderTest _provider = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context)
    {
        _postgres = new PostgreSqlBuilder(PostgresContainerOptions.DefaultImage).Build();
        await _postgres.StartAsync(context.CancellationToken);

        var connectionString = _postgres.GetConnectionString();
        _provider = new PostgresUnderTest(() => connectionString);
    }

    [ClassCleanup]
    public static async Task StopAsync()
    {
        await _provider.StopProcessesAsync();
        await _postgres.DisposeAsync();
    }

    [TestMethod]
    public async Task A_server_that_stops_answering_fires_LostToken_within_two_checks()
    {
        var a = _provider.NewProcess();
        var key = $"outage/{Guid.NewGuid():N}";
        var handle = (await a.TryAcquireAsync(key, Ct)).Value;
        var watch = Stopwatch.StartNew();

        await _postgres.PauseAsync(Ct);

        try
        {
            while (!handle.IsLost)
            {
                Assert.IsLessThan(_provider.Period * 2 + TimeSpan.FromSeconds(1), watch.Elapsed, "Still claiming a lock it cannot confirm.");
                await Task.Delay(20, Ct);
            }
        }
        finally
        {
            await _postgres.UnpauseAsync(Ct);
        }

        Assert.AreEqual(LockLossReason.ConnectionLost, handle.LossReason);

        // Disposing ends the session one way or another, so the key is free again for the next holder.
        await handle.DisposeAsync();
        var b = _provider.NewProcess();
        var next = await b.AcquireAsync(key, TimeSpan.FromSeconds(15), Ct);
        Assert.IsTrue(next.IsSuccess, next.ToString());
        await next.Value.DisposeAsync();
    }
}
