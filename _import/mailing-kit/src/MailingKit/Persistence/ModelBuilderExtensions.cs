using MailingKit.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace MailingKit.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>Maps the send log into the host's model. Call from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddMailing(this ModelBuilder builder, string schema = "email")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ApplyConfiguration(new EmailLogConfiguration(schema));
        return builder;
    }

    /// <summary>
    /// Maps the templates table. Only needed with database templates — a host using file templates
    /// should not call this, or it inherits a table it never reads.
    /// </summary>
    public static ModelBuilder AddEmailTemplates(this ModelBuilder builder, string schema = "email")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ApplyConfiguration(new EmailTemplateConfiguration(schema));
        return builder;
    }
}
