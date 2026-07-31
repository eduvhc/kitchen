using Npgsql;
using TestingKit.MSTest;

namespace TestingKit.Tests;

[TestClass]
public class PostgresFixtureTests : IntegrationTest
{
    protected override TestEnvironment Environment => TestHost.Environment;

    [TestMethod]
    public async Task Starts_a_container_and_reports_running()
    {
        Assert.IsTrue(TestHost.Postgres.IsRunning);
        Assert.IsFalse(TestHost.Postgres.IsExternal);
        Assert.Contains("Host=", TestHost.Postgres.ConnectionString);

        var one = await TestHost.Postgres.ScalarAsync<int>("SELECT 1", CancellationToken);
        Assert.AreEqual(1, one);
    }

    [TestMethod]
    public async Task Runs_setup_scripts_on_start()
    {
        var count = await TestHost.Postgres.CountAsync("products", CancellationToken);
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task Reset_removes_rows_written_by_a_test()
    {
        await TestHost.Postgres.ExecuteSqlAsync("INSERT INTO products (name) VALUES ('widget')", CancellationToken);
        Assert.AreEqual(1, await TestHost.Postgres.CountAsync("products", CancellationToken));

        await TestHost.Postgres.ResetAsync(CancellationToken);

        Assert.AreEqual(0, await TestHost.Postgres.CountAsync("products", CancellationToken));
    }

    [TestMethod]
    public async Task Reset_keeps_the_schema_intact()
    {
        await TestHost.Postgres.ResetAsync(CancellationToken);

        var exists = await TestHost.Postgres.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'products')",
            CancellationToken);

        Assert.IsTrue(exists);
    }

    [TestMethod]
    public async Task Opens_connections_callers_can_use_directly()
    {
        await using var connection = await TestHost.Postgres.CreateConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT current_database()", connection);

        var database = (string?)await command.ExecuteScalarAsync(CancellationToken);

        Assert.AreEqual("testdb", database);
    }

    [TestMethod]
    public async Task Publishes_its_connection_string_as_a_setting()
    {
        var settings = TestHost.Environment.BuildSettings();

        Assert.AreEqual(TestHost.Postgres.ConnectionString, settings["ConnectionStrings:Postgres"]);
        await Task.CompletedTask;
    }
}
