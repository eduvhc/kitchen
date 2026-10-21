using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LockingKit.Postgres.UnitTests;

[TestClass]
public sealed class PostgresRegistrationTests
{
    private const string ConnectionString = "Host=localhost;Database=app;Username=app;Password=secret";

    [TestMethod]
    public void Options_bind_from_the_locking_section_and_the_lock_is_connection_held()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Locking:Default:ConnectionString"] = ConnectionString,
                ["Locking:Default:HealthCheckInterval"] = "00:00:02",
                ["Locking:Default:FencingTokens"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLocking().UsePostgres(configuration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        var locks = (DistributedLock)provider.GetRequiredService<IDistributedLock>();
        Assert.AreEqual("postgres", locks.Provider);
        Assert.IsNull(locks.Timing.Duration, "No expiry: the session holds the lock.");
        Assert.AreEqual(TimeSpan.FromSeconds(2), locks.Timing.RenewalInterval);
    }

    [TestMethod]
    [DataRow(null, false, "exactly one of ConnectionString and DataSource")]
    [DataRow(ConnectionString, true, "exactly one of ConnectionString and DataSource")]
    [DataRow("Host=localhost;NoSuchSetting=1", false, "not a valid Npgsql connection string")]
    public void Invalid_settings_fail_at_startup_naming_the_setting(string? connectionString, bool dataSource, string message)
    {
        using var source = NpgsqlDataSource.Create(ConnectionString);
        var services = new ServiceCollection();
        services.AddLocking().UsePostgres(o =>
        {
            o.ConnectionString = connectionString;
            o.DataSource = dataSource ? source : null;
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, message);
    }

    [TestMethod]
    public void A_check_interval_below_100_ms_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddLocking().UsePostgres(o =>
        {
            o.ConnectionString = ConnectionString;
            o.HealthCheckInterval = TimeSpan.FromMilliseconds(50);
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        StringAssert.Contains(ex.Message, "HealthCheckInterval");
    }

    [TestMethod]
    public async Task A_data_source_the_host_owns_is_not_disposed_with_the_container()
    {
        await using var source = NpgsqlDataSource.Create(ConnectionString);
        var services = new ServiceCollection();
        services.AddLocking().UsePostgres(o => o.DataSource = source);

        await using (var provider = services.BuildServiceProvider())
        {
            _ = provider.GetRequiredService<IDistributedLock>();
        }

        // Still usable: creating a connection from a disposed data source throws.
        await using var connection = source.CreateConnection();
    }

    [TestMethod]
    [DataRow("28P01", false)]
    [DataRow("3D000", false)]
    [DataRow("42501", false)]
    [DataRow("57P01", true)]
    [DataRow("57P03", true)]
    [DataRow("53300", true)]
    [DataRow("08006", true)]
    [DataRow("42601", false)]
    public void Outages_are_transient_and_misconfiguration_throws(string sqlState, bool transient)
    {
        var error = PostgresAdvisoryLockBackend.Translate(new PostgresException("failed", "FATAL", "FATAL", sqlState));

        Assert.AreEqual(transient, error is { IsTransient: true }, sqlState);
    }
}
