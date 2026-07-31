using MailingKit.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace MailingKit.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Maps the emails table into the host's model. Call from <c>OnModelCreating</c>, or let
    /// <c>AddEmailing&lt;TContext&gt;</c> apply it for you.
    /// </summary>
    public static ModelBuilder AddEmailing(this ModelBuilder builder, string schema = "email")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ApplyConfiguration(new EmailMessageConfiguration(schema));
        return builder;
    }

    /// <summary>
    /// Maps the templates table. Only needed when templates are stored in the database — a host
    /// using file templates should not call this, or it inherits a table it never reads.
    /// </summary>
    public static ModelBuilder AddEmailTemplates(this ModelBuilder builder, string schema = "email")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ApplyConfiguration(new EmailTemplateConfiguration(schema));
        return builder;
    }
}
