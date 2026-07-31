using MailingKit.Templates;
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

    public async Task<EmailTemplate> UpsertAsync(EmailTemplate emailTemplate, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var existing = await Templates.FirstOrDefaultAsync(t => t.Key == emailTemplate.Key, ct);

        if (existing is null)
        {
            emailTemplate.CreatedAt = now;
            emailTemplate.UpdatedAt = now;
            Templates.Add(emailTemplate);
            await db.SaveChangesAsync(ct);
            return emailTemplate;
        }

        existing.Description = emailTemplate.Description;
        existing.SubjectTemplate = emailTemplate.SubjectTemplate;
        existing.HtmlTemplate = emailTemplate.HtmlTemplate;
        existing.TextTemplate = emailTemplate.TextTemplate;
        existing.FromAddress = emailTemplate.FromAddress;
        existing.FromName = emailTemplate.FromName;
        existing.IsActive = emailTemplate.IsActive;
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
