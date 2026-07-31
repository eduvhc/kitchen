using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestingKit.EntityFramework;

public static class MigrationExtensions
{
    public static async Task MigrateAsync<TContext>(
        this ITestFixture fixture,
        Func<string, TContext> contextFactory,
        CancellationToken ct = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(contextFactory);

        await using var context = contextFactory(fixture.ConnectionString);
        await context.Database.MigrateAsync(ct);
    }

    public static async Task EnsureHistoryTableAsync<TContext>(this TContext context, CancellationToken ct = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);

        var history = context.GetService<IHistoryRepository>();
        var script = history.GetCreateIfNotExistsScript();
        await context.Database.ExecuteSqlRawAsync(script, ct);
    }

    public static async Task<IReadOnlyList<string>> GetPendingMigrationsAsync<TContext>(
        this TContext context,
        CancellationToken ct = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. await context.Database.GetPendingMigrationsAsync(ct)];
    }
}
