using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace TestingKit.Postgres;

public class PostgresFixture(
    PostgresContainerOptions? containerOptions = null,
    PostgresClientOptions? clientOptions = null)
    : TestFixtureBase<PostgresContainerOptions, PostgresClientOptions>(containerOptions, clientOptions), IResettableFixture
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    public async Task<NpgsqlConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        EnsureReady();
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = await CreateConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> ScalarAsync<T>(string sql, CancellationToken ct = default)
    {
        await using var connection = await CreateConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? default : (T)value;
    }

    public async Task<long> CountAsync(string table, CancellationToken ct = default) =>
        await ScalarAsync<long>($"SELECT COUNT(*) FROM {table}", ct);

    public async Task SnapshotAsync(CancellationToken ct = default)
    {
        EnsureReady();

        var options = new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [.. Client.SchemasToInclude],
            TablesToIgnore = [.. Client.TablesToIgnore.Select(table => new Table(table))],
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
        var builder = new PostgreSqlBuilder(Container.Image!)
            .WithDatabase(Container.Database)
            .WithUsername(Container.Username)
            .WithPassword(Container.Password)
            .WithReuse(Container.Reuse);

        foreach (var (key, value) in Container.Labels)
        {
            builder = builder.WithLabel(key, value);
        }

        if (Container.Commands.Count > 0)
        {
            builder = builder.WithCommand([.. Container.Commands]);
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
}
