using MailingKit.Templates;

namespace MailingKit.UnitTests;

public sealed class FakeTemplateStore : ITemplateStore
{
    public Dictionary<string, EmailTemplate> Templates { get; } = [];

    public Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Templates.GetValueOrDefault(key));
}
