using Microsoft.Data.SqlClient;
using Respawn;
using Respawn.Graph;
using Testcontainers.MsSql;

namespace TestingKit.SqlServer;

public sealed class SqlServerContainerOptions : ContainerOptions
{
    public const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest";

    public SqlServerContainerOptions() => Image = DefaultImage;

    public string Password { get; set; } = "Strong_password_123!";
}

public sealed class SqlServerClientOptions : ClientOptions
{
    public IList<string> SetupScripts { get; } = [];

    public IList<string> SchemasToInclude { get; } = [];

    public IList<string> TablesToIgnore { get; } = [];
}

public class SqlServerFixture(
    SqlServerContainerOptions? containerOptions = null,
    SqlServerClientOptions? clientOptions = null)
    : TestFixtureBase<SqlServerContainerOptions, SqlServerClientOptions>(containerOptions, clientOptions), IResettableFixture
{
    private MsSqlContainer? _container;
    private Respawner? _respawner;

    public async Task<SqlConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        EnsureReady();
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = await CreateConnectionAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> ScalarAsync<T>(string sql, CancellationToken ct = default)
    {
        await using var connection = await CreateConnectionAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? default : (T)value;
    }

    public async Task EnsureDatabaseExistsAsync(CancellationToken ct = default)
    {
        EnsureReady();

        var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;

        await using var connection = new SqlConnection(master);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(
            $"IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'{database}') CREATE DATABASE [{database}]",
            connection);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SnapshotAsync(CancellationToken ct = default)
    {
        EnsureReady();

        var options = new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            SchemasToInclude = [.. Client.SchemasToInclude],
            TablesToIgnore = ParseTables(Client.TablesToIgnore),
        };

        await using var connection = await CreateConnectionAsync(ct);
        _respawner = await Respawner.CreateAsync(connection, options);
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (_respawner is null)
        {
            return;
        }

        await using var connection = await CreateConnectionAsync(ct);
        await _respawner.ResetAsync(connection);
    }

    protected override async Task StartContainerAsync(CancellationToken ct)
    {
        var builder = new MsSqlBuilder(Container.Image!)
            .WithPassword(Container.Password)
            .WithReuse(Container.Reuse);

        foreach (var (key, value) in Container.Labels)
        {
            builder = builder.WithLabel(key, value);
        }

        _container = builder.Build();
        await _container.StartAsync(ct);
        ConnectionString = _container.GetConnectionString();
    }

    protected override async ValueTask DisposeContainerAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override async Task OnAfterStartAsync(CancellationToken ct)
    {
        foreach (var script in Client.SetupScripts)
        {
            await ExecuteSqlAsync(script, ct);
        }
    }

    private static Table[] ParseTables(IEnumerable<string> names) =>
        [.. names.Select(name =>
        {
            var parts = name.Split('.', 2);
            return parts.Length == 2 ? new Table(parts[0], parts[1]) : new Table(name);
        })];

}
