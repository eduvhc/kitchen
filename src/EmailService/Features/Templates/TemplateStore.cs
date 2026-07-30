using EmailService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmailService.Features.Templates;

public class TemplateStore(EmailDbContext db, TimeProvider clock) : ITemplateStore
{
    public Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key, ct);

    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        await db.Templates.AsNoTracking().OrderBy(t => t.Key).ToListAsync(ct);

    public async Task<EmailTemplate> UpsertAsync(EmailTemplate template, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var existing = await db.Templates.FirstOrDefaultAsync(t => t.Key == template.Key, ct);

        if (existing is null)
        {
            template.CreatedAt = now;
            template.UpdatedAt = now;
            db.Templates.Add(template);
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
        var affected = await db.Templates.Where(t => t.Key == key).ExecuteDeleteAsync(ct);
        return affected > 0;
    }
}
