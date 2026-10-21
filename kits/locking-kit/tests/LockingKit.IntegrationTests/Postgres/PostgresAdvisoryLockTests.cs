using System.Diagnostics;
using LockingKit.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LockingKit.IntegrationTests;

/// <summary>What only the PostgreSQL provider does: the documented key mapping, sessions, and killed connections.</summary>
[TestClass]
public sealed class PostgresAdvisoryLockTests
{
    private static readonly PostgresUnderTest Provider = new(() => TestHost.Postgres.ConnectionString);

    private string _prefix = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestInitialize]
    public void UseFreshKeys() => _prefix = $"pg/{Guid.NewGuid():N}/";

    [TestCleanup]
    public Task StopProcessesAsync() => Provider.StopProcessesAsync();

    [TestMethod]
    public async Task The_documented_sql_computes_the_same_advisory_key()
    {
        await using var connection = await TestHost.Postgres.CreateConnectionAsync(Ct);

        foreach (var key in new[] { "orders/42", "jobs/nightly", "café 🔒", "", new string('x', 512) })
        {
            await using var command = new NpgsqlCommand($"SELECT {PostgresLockKeys.SqlExpression}", connection);
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = key });

            Assert.AreEqual(PostgresLockKeys.ToAdvisoryKey(key), (long)(await command.ExecuteScalarAsync(Ct))!, key);
        }
    }

    [TestMethod]
    public async Task A_lock_taken_in_plain_sql_excludes_LockingKit_and_the_other_way_round()
    {
        var locks = Provider.NewProcess();
        var key = _prefix + "shared-with-sql";
        await using var connection = await TestHost.Postgres.CreateConnectionAsync(Ct);

        Assert.IsTrue(await ScalarAsync<bool>(connection, $"SELECT pg_try_advisory_lock({PostgresLockKeys.SqlExpression})", key));
        Assert.AreEqual(LockingErrors.Busy, (await locks.TryAcquireAsync(key, Ct)).Error?.Code);
        Assert.IsTrue(await ScalarAsync<bool>(connection, $"SELECT pg_advisory_unlock({PostgresLockKeys.SqlExpression})", key));

        await using var handle = (await locks.TryAcquireAsync(key, Ct)).Value;
        Assert.IsFalse(await ScalarAsync<bool>(connection, $"SELECT pg_try_advisory_lock({PostgresLockKeys.SqlExpression})", key));
    }

    [TestMethod]
    public async Task A_released_lock_leaves_no_advisory_lock_on_the_pooled_connection()
    {
        var locks = Provider.NewProcess();
        var key = _prefix + "pooled";

        for (var i = 0; i < 5; i++)
        {
            await using var handle = (await locks.TryAcquireAsync(key, Ct)).Value;
            Assert.AreEqual(1L, await CountAdvisoryLocksAsync(key));
        }

        Assert.AreEqual(0L, await CountAdvisoryLocksAsync(key), "No pooled session still holds it.");
    }

    [TestMethod]
    public async Task Killing_the_holder_s_connection_frees_the_lock_at_once_and_fires_LostToken_within_one_check()
    {
        var a = Provider.NewProcess();
        var b = Provider.NewProcess();
        var key = _prefix + "killed";
        var handle = (await a.TryAcquireAsync(key, Ct)).Value;
        var pid = ((PostgresAdvisoryLease)FaultyBackend.Unwrap(handle)).ProcessId;

        var watch = Stopwatch.StartNew();
        await TestHost.Postgres.ExecuteSqlAsync($"SELECT pg_terminate_backend({pid})", Ct);

        // The server frees it with the session; the old holder can only find out at its next check.
        var next = await b.AcquireAsync(key, TimeSpan.FromSeconds(5), Ct);
        Assert.IsTrue(next.IsSuccess, next.ToString());

        while (!handle.IsLost)
        {
            Assert.IsLessThan(Provider.Period * 2 + TimeSpan.FromSeconds(2), watch.Elapsed, "The holder never noticed.");
            await Task.Delay(20, Ct);
        }

        Assert.AreEqual(LockLossReason.ConnectionLost, handle.LossReason);
        await handle.DisposeAsync();
        await next.Value.DisposeAsync();
    }

    [TestMethod]
    public async Task Fencing_tokens_are_transaction_ids_that_only_grow()
    {
        var locks = Provider.NewProcess();
        var key = _prefix + "fenced";

        await using var first = (await locks.TryAcquireAsync(key, Ct)).Value;
        var current = await TestHost.Postgres.ScalarAsync<long>("SELECT pg_current_xact_id()::text::bigint", Ct);

        Assert.IsLessThan(current, first.FencingToken!.Value);
    }

    [TestMethod]
    public async Task Registration_from_configuration_wires_a_working_lock()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Locking:Default:ConnectionString"] = TestHost.Postgres.ConnectionString,
                ["Locking:Default:HealthCheckInterval"] = "00:00:01",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLocking().UsePostgres(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();

        var locks = provider.GetRequiredService<IDistributedLock>();
        await using var handle = (await locks.TryAcquireAsync(_prefix + "configured", Ct)).Value;

        Assert.IsNull(handle.FencingToken, "Fencing is opt-in on PostgreSQL.");
        Assert.AreEqual(LockingErrors.Busy, (await locks.TryAcquireAsync(_prefix + "configured", Ct)).Error?.Code);
    }

    [TestMethod]
    public async Task Wrong_credentials_are_a_misconfiguration_result()
    {
        var builder = new NpgsqlConnectionStringBuilder(TestHost.Postgres.ConnectionString) { Password = "wrong" };
        var services = new ServiceCollection();
        services.AddLocking().UsePostgres(o => o.ConnectionString = builder.ConnectionString);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDistributedLock>().TryAcquireAsync(_prefix + "k", Ct);

        Assert.AreEqual(LockingErrors.Misconfigured, result.Error?.Code);
        StringAssert.Contains(result.Error!.Message, "28P01");
    }

    private async Task<long> CountAdvisoryLocksAsync(string key)
    {
        var advisory = PostgresLockKeys.ToAdvisoryKey(key);
        return await TestHost.Postgres.ScalarAsync<long>(
            $"SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND granted AND objsubid = 1 "
            + $"AND classid = {(advisory >> 32) & 0xFFFF_FFFF}::bigint::oid AND objid = {advisory & 0xFFFF_FFFF}::bigint::oid",
            Ct);
    }

    private async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, string key)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = key });
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
