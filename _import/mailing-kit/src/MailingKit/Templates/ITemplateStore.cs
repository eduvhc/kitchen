namespace MailingKit.Templates;

/// <summary>What the send path needs. Every store satisfies this, including read-only ones.</summary>
public interface ITemplateStore
{
    Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Editing operations. Only the database store implements this — file templates are edited in the
/// host's repository, not through the library.
/// </summary>
public interface IWritableTemplateStore : ITemplateStore
{
    Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default);

    Task<EmailTemplate> UpsertAsync(EmailTemplate template, CancellationToken ct = default);

    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}
