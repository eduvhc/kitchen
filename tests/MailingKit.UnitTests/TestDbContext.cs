using MailingKit.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailingKit.UnitTests;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.AddMailing();
        modelBuilder.AddEmailTemplates();
    }
}
