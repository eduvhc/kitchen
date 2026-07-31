using MailingKit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailingKit.Persistence.Configurations;

internal sealed class EmailLogConfiguration(string schema) : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> entity)
    {
        entity.ToTable("email_log", schema);
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.MessageId).HasColumnName("message_id");
        entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(100);
        entity.Property(e => e.FromAddress).HasColumnName("from_address").HasMaxLength(320).IsRequired();
        entity.Property(e => e.FromName).HasColumnName("from_name").HasMaxLength(200);
        entity.Property(e => e.ReplyTo).HasColumnName("reply_to").HasMaxLength(320);
        entity.Property(e => e.To).HasColumnName("to_addresses").HasColumnType("text[]").IsRequired();
        entity.Property(e => e.Cc).HasColumnName("cc_addresses").HasColumnType("text[]");
        entity.Property(e => e.Bcc).HasColumnName("bcc_addresses").HasColumnType("text[]");
        entity.Property(e => e.Subject).HasColumnName("subject").HasMaxLength(1000).IsRequired();
        entity.Property(e => e.TemplateKey).HasColumnName("template_key").HasMaxLength(200);
        entity.Property(e => e.Status).HasColumnName("status").HasConversion<int>();
        entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
        entity.Property(e => e.SentAt).HasColumnName("sent_at");
        entity.Property(e => e.LastError).HasColumnName("last_error");
        entity.Property(e => e.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(500);
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");

        // One row per message, so a redelivered handle updates rather than duplicates.
        entity.HasIndex(e => e.MessageId).IsUnique();
        entity.HasIndex(e => e.CreatedAt);
        entity.HasIndex(e => e.TemplateKey);
    }
}
