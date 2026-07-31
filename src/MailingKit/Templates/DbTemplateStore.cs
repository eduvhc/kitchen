using MailingKit.Templates.Abstractions;
using MailingKit.Templates.Domain;
using Microsoft.EntityFrameworkCore;

namespace MailingKit.Templates;

internal sealed class DbTemplateStore<TContext>(TContext db, TimeProvider clock) : IWritableTemplateStore
    where TContext : DbContext
{
    private DbSet<EmailTemplate> Templates => db.Set<EmailTemplate>();

    public Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key, ct);

    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        await Templates.AsNoTracking().OrderBy(t => t.Key).ToListAsync(ct);

    public async Task<EmailTemplate> UpsertAsync(EmailTemplate template, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        var now = clock.GetUtcNow();
        var existing = await Templates.FirstOrDefaultAsync(t => t.Key == template.Key, ct);

        if (existing is null)
        {
            template.CreatedAt = now;
            template.UpdatedAt = now;
            Templates.Add(template);
            await db.SaveChangesAsync(ct);
            return template;
        }

        existing.Description = template.Description;
        existing.SubjectTemplate = template.SubjectTemplate;
        existing.HtmlTemplate = template.HtmlTemplate;
        existing.TextTemplate = template.TextTemplate;
        existing.FromAddress = template.FromAddress;
        existing.FromName = template.FromName;
        existing.IsActive = template.IsActive;
        existing.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var affected = await Templates.Where(t => t.Key == key).ExecuteDeleteAsync(ct);
        return affected > 0;
    }
}
