using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LockingKit.Postgres;

/// <summary>Backs lock providers with PostgreSQL advisory locks.</summary>
public static class PostgresLockingBuilderExtensions
{
    /// <summary>The provider name reported as <c>lockingkit.provider</c>.</summary>
    public const string ProviderName = "postgres";

    /// <summary>
    /// Backs the provider with advisory locks, with options bound from <c>Locking:{name}</c> of
    /// <paramref name="configuration"/>. Options are validated at startup.
    /// </summary>
    /// <param name="builder">The builder from <c>AddLocking</c>.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The builder.</returns>
    public static LockingBuilder UsePostgres(this LockingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddOptions<PostgresLockOptions>(builder.Name)
            .Bind(configuration.GetSection(DistributedLocks.SectionPath(builder.Name)));

        return UseCore(builder);
    }

    /// <summary>Backs the provider with advisory locks, with options set in code. Options are validated at startup.</summary>
    /// <param name="builder">The builder from <c>AddLocking</c>.</param>
    /// <param name="configure">Sets <see cref="PostgresLockOptions"/>.</param>
    /// <returns>The builder.</returns>
    public static LockingBuilder UsePostgres(this LockingBuilder builder, Action<PostgresLockOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddOptions<PostgresLockOptions>(builder.Name).Configure(configure);
        return UseCore(builder);
    }

    internal static PostgresAdvisoryLockBackend CreateBackend(PostgresLockOptions options) =>
        options.DataSource is { } dataSource
            ? new PostgresAdvisoryLockBackend(dataSource, ownsDataSource: false, options.HealthCheckInterval, options.FencingTokens)
            : new PostgresAdvisoryLockBackend(
                NpgsqlDataSource.Create(options.ConnectionString!),
                ownsDataSource: true,
                options.HealthCheckInterval,
                options.FencingTokens);

    private static LockingBuilder UseCore(LockingBuilder builder)
    {
        var path = DistributedLocks.SectionPath(builder.Name);

        builder.Services.AddOptions<PostgresLockOptions>(builder.Name)
            .Validate(
                o => string.IsNullOrWhiteSpace(o.ConnectionString) != (o.DataSource is null),
                $"{path}: set exactly one of ConnectionString and DataSource.")
            .Validate(
                o => o.DataSource is not null || IsParseable(o.ConnectionString),
                $"{path}:ConnectionString is not a valid Npgsql connection string.")
            .Validate(
                o => o.HealthCheckInterval >= TimeSpan.FromMilliseconds(100),
                $"{path}:HealthCheckInterval must be at least 100 ms.");

        return builder.UseBackend<PostgresLockOptions>(ProviderName, (_, o) => CreateBackend(o));
    }

    private static bool IsParseable(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
