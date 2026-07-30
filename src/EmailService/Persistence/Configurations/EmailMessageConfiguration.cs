using System.Text.Json;
using EmailService.Features.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EmailService.Persistence.Configurations;

public class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<EmailMessage> entity)
    {
        entity.ToTable("emails");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(100);
        entity.Property(e => e.FromAddress).HasColumnName("from_address").HasMaxLength(320).IsRequired();
        entity.Property(e => e.FromName).HasColumnName("from_name").HasMaxLength(200);
        entity.Property(e => e.ReplyTo).HasColumnName("reply_to").HasMaxLength(320);
        entity.Property(e => e.To).HasColumnName("to_addresses").HasColumnType("text[]").IsRequired();
        entity.Property(e => e.Cc).HasColumnName("cc_addresses").HasColumnType("text[]");
        entity.Property(e => e.Bcc).HasColumnName("bcc_addresses").HasColumnType("text[]");
        entity.Property(e => e.Subject).HasColumnName("subject").HasMaxLength(1000).IsRequired();
        entity.Property(e => e.HtmlBody).HasColumnName("html_body");
        entity.Property(e => e.TextBody).HasColumnName("text_body");

        entity.Property(e => e.Attachments)
            .HasColumnName("attachments")
            .HasColumnType("jsonb")
            .HasConversion(JsonConverter<List<EmailAttachment>>(), JsonComparer<List<EmailAttachment>>());

        entity.Property(e => e.Headers)
            .HasColumnName("headers")
            .HasColumnType("jsonb")
            .HasConversion(JsonConverter<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>());

        entity.Property(e => e.TemplateKey).HasColumnName("template_key").HasMaxLength(200);
        entity.Property(e => e.Status).HasColumnName("status").HasConversion<int>();
        entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
        entity.Property(e => e.MaxAttempts).HasColumnName("max_attempts");
        entity.Property(e => e.ScheduledAt).HasColumnName("scheduled_at");
        entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
        entity.Property(e => e.SentAt).HasColumnName("sent_at");
        entity.Property(e => e.LastError).HasColumnName("last_error");
        entity.Property(e => e.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(500);
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        entity.HasIndex(e => e.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        entity.HasIndex(e => new { e.Status, e.ScheduledAt });
        entity.HasIndex(e => e.CreatedAt);
        entity.HasIndex(e => e.TemplateKey);
    }

    private static ValueConverter<T, string> JsonConverter<T>()
        where T : new() =>
        new(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<T>(v, JsonOptions) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() =>
        new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);
}
