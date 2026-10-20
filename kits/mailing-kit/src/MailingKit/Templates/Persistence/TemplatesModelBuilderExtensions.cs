using Microsoft.EntityFrameworkCore;

namespace MailingKit.Templates;

public static class TemplatesModelBuilderExtensions
{
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
