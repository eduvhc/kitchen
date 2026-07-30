namespace EmailService.Features.Templates;

public interface ITemplateStore
{
    Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default);

    Task<EmailTemplate> UpsertAsync(EmailTemplate template, CancellationToken ct = default);

    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}
