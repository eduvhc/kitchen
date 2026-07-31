using MailingKit.Options;
using MailingKit.Templates.Abstractions;
using MailingKit.Templates.Domain;
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

    public FileTemplateStore(IOptions<MailingKitOptions> options)
    {
        var templates = options.Value.Templates;
        _directory = Path.GetFullPath(templates.Directory);
        _extension = templates.Extension;
    }

    public async Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // A key becomes part of a path, so anything that could climb out of the directory is refused
        // rather than sanitised — templates are named by the host, not by end users.
        if (key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '.'))
        {
            throw new ValidationException($"Template key '{key}' contains unsupported characters.");
        }

        if (key.Contains("..", StringComparison.Ordinal))
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
        var path = Path.Combine(_directory, $"{key}.{part}.{_extension}");

        // Belt and braces: the key was validated above, but confirm the resolved path stayed put.
        var resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ValidationException($"Template key '{key}' resolves outside the template directory.");
        }

        return File.Exists(resolved) ? await File.ReadAllTextAsync(resolved, ct) : null;
    }
}
