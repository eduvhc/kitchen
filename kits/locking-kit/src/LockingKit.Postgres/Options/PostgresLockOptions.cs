using Npgsql;

namespace LockingKit.Postgres;

/// <summary>
/// Settings for locks held as PostgreSQL advisory locks. Bound from <c>Locking:{name}</c> by
/// <c>UsePostgres(configuration)</c>.
/// </summary>
/// <remarks>
/// Every held lock keeps one connection open for as long as it is held, so size the pool (<c>Maximum Pool Size</c>)
/// for the locks a process holds at once. Advisory locks are per database: every process that should exclude another
/// must connect to the same database. Locks taken through PgBouncer in transaction or statement pooling mode do not
/// work; connect directly or use session pooling.
/// </remarks>
public sealed class PostgresLockOptions : LockingOptions
{
    /// <summary>
    /// An Npgsql connection string. LockingKit builds its own data source from it. Keep it in a secret store or an
    /// environment variable, never in <c>appsettings.json</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// An existing data source to open lock connections from, instead of <see cref="ConnectionString"/>. Code only;
    /// LockingKit does not dispose it.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// How often a held lock confirms that its connection is alive and still holds it. A lock whose connection fails,
    /// or whose check does not answer within this interval, is reported lost. At least 100 ms; defaults to 5 s.
    /// </summary>
    /// <remarks>
    /// A session-level advisory lock has no expiry: the server frees it the moment the session ends. This interval is
    /// therefore how late a holder can learn that the server already gave its lock to someone else.
    /// </remarks>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Issues a <see cref="ILockHandle.FencingToken"/> with every lock: the 64-bit transaction id
    /// (<c>pg_current_xact_id()</c>) assigned right after acquiring, which only grows. Needs PostgreSQL 13 or later
    /// and a writable primary, and consumes one transaction id per acquisition. Defaults to <see langword="false"/>.
    /// </summary>
    public bool FencingTokens { get; set; }
}
