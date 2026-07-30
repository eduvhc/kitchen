using EmailService.Features.Templates;

namespace EmailService.Tests.TestDoubles;

public class FakeTemplateStore : ITemplateStore
{
    public Dictionary<string, EmailTemplate> Templates { get; } = [];

    public Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Templates.GetValueOrDefault(key));

    public Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmailTemplate>>(Templates.Values.ToList());

    public Task<EmailTemplate> UpsertAsync(EmailTemplate template, CancellationToken ct = default)
    {
        Templates[template.Key] = template;
        return Task.FromResult(template);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Templates.Remove(key));
}
