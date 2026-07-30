using Microsoft.EntityFrameworkCore;

namespace EmailService.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EmailDbContext>(builder =>
            builder.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__migrations", "email")));

        return services;
    }

    public static async Task MigrateAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EmailDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
