using Npgsql;
using ResultingKit;

namespace LockingKit.Postgres;

/// <summary>
/// Locks as session-level advisory locks (<c>pg_try_advisory_lock</c>), each held on its own connection. The server
/// frees a lock the moment its session ends, so a crashed holder never leaves a lock behind.
/// </summary>
/// <remarks>
/// A connection that may still hold a lock is never returned to the pool: when unlocking fails or cannot be confirmed,
/// the pool is told to close it instead (<see cref="NpgsqlConnection.ClearPool"/>), which ends the session server-side.
/// </remarks>
internal sealed class PostgresAdvisoryLockBackend : ILockBackend
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly bool _fencing;

    public PostgresAdvisoryLockBackend(NpgsqlDataSource dataSource, bool ownsDataSource, TimeSpan healthCheckInterval, bool fencingTokens)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        _dataSource = dataSource;
        _ownsDataSource = ownsDataSource;
        _fencing = fencingTokens;
        Timing = LeaseTiming.ConnectionHeld(healthCheckInterval);
    }

    public LeaseTiming Timing { get; }

    public async Task<ILockLease?> TryAcquireAsync(string key, CancellationToken ct)
    {
        var advisoryKey = PostgresLockKeys.ToAdvisoryKey(key);
        var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var lease = new PostgresAdvisoryLease(connection, advisoryKey);

        try
        {
            if (!await lease.TryLockAsync(ct).ConfigureAwait(false))
            {
                // Nothing was taken, so the connection goes back to the pool clean.
                await lease.ReturnUnlockedAsync().ConfigureAwait(false);
                return null;
            }

            if (_fencing)
            {
                await lease.IssueFencingTokenAsync(ct).ConfigureAwait(false);
            }

            return lease;
        }
        catch
        {
            // The lock may have been granted before the failure: end the session rather than pool it.
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Error? TranslateException(Exception exception) => Translate(exception);

    public async ValueTask DisposeAsync()
    {
        if (_ownsDataSource)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static Error? Translate(Exception exception) => exception switch
    {
        // Wrong credentials, a missing database, a denied permission: misconfiguration, so it throws.
        PostgresException { SqlState: "28P01" or "28000" or "3D000" or "42501" } => null,
        NpgsqlException { IsTransient: true } => LockingErrors.BackendUnavailable(exception.Message),

        // Connection exceptions, insufficient resources, operator intervention (shutdown, failover).
        PostgresException { SqlState: ['0', '8', ..] or ['5', '3', ..] or ['5', '7', 'P', ..] } =>
            LockingErrors.BackendUnavailable(exception.Message),
        _ => null,
    };
}

/// <summary>One advisory lock and the connection whose session holds it.</summary>
internal sealed class PostgresAdvisoryLease(NpgsqlConnection connection, long advisoryKey) : ILockLease
{
    private const string LockSql = "SELECT pg_try_advisory_lock($1)";
    private const string UnlockSql = "SELECT pg_advisory_unlock($1)";
    private const string FenceSql = "SELECT pg_current_xact_id()::text::bigint";

    // A bigint advisory key is stored as classid = high 32 bits, objid = low 32 bits, objsubid = 1.
    private const string CheckSql =
        "SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid() AND granted "
        + "AND objsubid = 1 AND classid = $1::bigint::oid AND objid = $2::bigint::oid)";

    private bool _unlocked;
    private int _disposed;

    public long AdvisoryKey { get; } = advisoryKey;

    public long? FencingToken { get; private set; }

    /// <summary>The server process holding the lock, for <c>pg_terminate_backend</c> in tests and diagnostics.</summary>
    public int ProcessId => connection.ProcessID;

    public async Task<bool> TryLockAsync(CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(LockSql, connection);
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = AdvisoryKey });
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task IssueFencingTokenAsync(CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(FenceSql, connection);
        FencingToken = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<bool> RenewAsync(CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(CheckSql, connection);
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = (AdvisoryKey >> 32) & 0xFFFF_FFFF });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = AdvisoryKey & 0xFFFF_FFFF });
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task ReleaseAsync(CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(UnlockSql, connection);
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = AdvisoryKey });
        await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

        // True or false, this session no longer holds the key, so the connection can be pooled again.
        _unlocked = true;
    }

    public ValueTask ReturnUnlockedAsync()
    {
        _unlocked = true;
        return DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (!_unlocked)
        {
            // Unknown state: have the pool close this connection, which ends the session and frees any lock it holds.
            NpgsqlConnection.ClearPool(connection);
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
