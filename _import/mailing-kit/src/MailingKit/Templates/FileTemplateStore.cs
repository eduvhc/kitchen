using MailingKit.Options;
using Microsoft.Extensions.Options;

namespace MailingKit.Templates;

/// <summary>
/// Reads templates from disk using a <c>{key}.{part}.{extension}</c> convention:
/// <c>welcome.subject.scriban</c> (required), <c>welcome.html.scriban</c>, <c>welcome.text.scriban</c>.
/// </summary>
internal sealed class FileTemplateStore : ITemplateStore
{
    private readonly string _directory;
    private readonly string _extension;

    public FileTemplateStore(IOptions<MailingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var templates = options.Value.Templates;
        _directory = Path.GetFullPath(templates.Directory);
        _extension = templates.Extension;
    }

    public async Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // A key becomes part of a path. Templates are named by the host, not by end users, so an
        // unexpected character is refused rather than sanitised.
        if (key.Contains("..", StringComparison.Ordinal)
            || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '.'))
        {
            throw new ValidationException($"Template key '{key}' is not a valid name.");
        }

        var subject = await ReadPartAsync(key, "subject", ct);

        if (subject is null)
        {
            return null;
        }

        return new EmailTemplate
        {
            Key = key,
            SubjectTemplate = subject,
            HtmlTemplate = await ReadPartAsync(key, "html", ct),
            TextTemplate = await ReadPartAsync(key, "text", ct),
            IsActive = true,
        };
    }

    private async Task<string?> ReadPartAsync(string key, string part, CancellationToken ct)
    {
        var resolved = Path.GetFullPath(Path.Combine(_directory, $"{key}.{part}.{_extension}"));

        // Belt and braces: the key was validated above, but confirm the path stayed inside the root.
        if (!resolved.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ValidationException($"Template key '{key}' resolves outside the template directory.");
        }

        return File.Exists(resolved) ? await File.ReadAllTextAsync(resolved, ct) : null;
    }
}
