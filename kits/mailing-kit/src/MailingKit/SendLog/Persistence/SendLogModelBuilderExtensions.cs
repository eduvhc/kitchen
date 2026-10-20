using Microsoft.EntityFrameworkCore;

namespace MailingKit.SendLog;

public static class SendLogModelBuilderExtensions
{
    /// <summary>Maps the send log into the host's model. Call from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddMailing(this ModelBuilder builder, string schema = "email")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ApplyConfiguration(new EmailLogConfiguration(schema));
        return builder;
    }
}
