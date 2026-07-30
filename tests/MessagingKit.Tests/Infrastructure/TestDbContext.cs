using MessagingKit.Inbox.Persistence;
using MessagingKit.Outbox.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MessagingKit.Tests.Infrastructure;

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices", "billing");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.Reference).HasColumnName("reference").IsRequired();
        });

        modelBuilder.AddOutbox();
        modelBuilder.AddInbox();
    }
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Reference { get; set; }
}

public sealed record SendEmail(string To, string Subject);
