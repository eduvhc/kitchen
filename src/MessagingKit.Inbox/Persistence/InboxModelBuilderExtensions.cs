using System.Text.Json;
using MessagingKit.Inbox.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MessagingKit.Inbox.Persistence;

public static class InboxModelBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ModelBuilder AddInbox(this ModelBuilder builder, string schema = "messaging", string table = "inbox")
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable(table, schema);
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(m => m.Type).HasColumnName("type").HasMaxLength(300).IsRequired();
            entity.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

            entity.Property(m => m.Headers)
                .HasColumnName("headers")
                .HasColumnType("jsonb")
                .HasConversion(
                    new ValueConverter<Dictionary<string, string>, string>(
                        v => JsonSerializer.Serialize(v, JsonOptions),
                        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new Dictionary<string, string>()),
                    new ValueComparer<Dictionary<string, string>>(
                        (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                        v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                        v => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));

            entity.Property(m => m.Status).HasColumnName("status").HasConversion<int>();
            entity.Property(m => m.AttemptCount).HasColumnName("attempt_count");
            entity.Property(m => m.MaxAttempts).HasColumnName("max_attempts");
            entity.Property(m => m.ScheduledAt).HasColumnName("scheduled_at");
            entity.Property(m => m.LockedUntil).HasColumnName("locked_until");
            entity.Property(m => m.ReceivedAt).HasColumnName("received_at");
            entity.Property(m => m.ProcessedAt).HasColumnName("processed_at");
            entity.Property(m => m.LastError).HasColumnName("last_error");
            entity.Property(m => m.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(m => new { m.Status, m.ScheduledAt });
            entity.HasIndex(m => m.ReceivedAt);
        });

        return builder;
    }
}
