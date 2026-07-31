using MailingKit.Templates;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace MailingKit.Persistence.Configurations;

internal sealed class EmailTemplateConfiguration(string schema) : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> entity)
    {
        entity.ToTable("templates", schema);
        entity.HasKey(t => t.Id);

        entity.Property(t => t.Id).HasColumnName("id");
        entity.Property(t => t.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        entity.Property(t => t.Description).HasColumnName("description").HasMaxLength(1000);
        entity.Property(t => t.SubjectTemplate).HasColumnName("subject_template").IsRequired();
        entity.Property(t => t.HtmlTemplate).HasColumnName("html_template");
        entity.Property(t => t.TextTemplate).HasColumnName("text_template");
        entity.Property(t => t.FromAddress).HasColumnName("from_address").HasMaxLength(320);
        entity.Property(t => t.FromName).HasColumnName("from_name").HasMaxLength(200);
        entity.Property(t => t.IsActive).HasColumnName("is_active");
        entity.Property(t => t.CreatedAt).HasColumnName("created_at");
        entity.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        entity.HasIndex(t => t.Key).IsUnique();
    }
}
