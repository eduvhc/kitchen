using EmailService.Features.Emails.Domain;
using EmailService.Features.Templates.Domain;
using MessagingKit.Inbox.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmailService.Persistence;

public class EmailDbContext(DbContextOptions<EmailDbContext> options) : DbContext(options)
{
    public DbSet<EmailMessage> Emails => Set<EmailMessage>();
    public DbSet<EmailTemplate> Templates => Set<EmailTemplate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("email");
        builder.ApplyConfigurationsFromAssembly(typeof(EmailDbContext).Assembly);
        builder.AddInbox(schema: "email");
    }
}
