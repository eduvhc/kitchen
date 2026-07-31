namespace MailingKit.Options;

public enum TemplateStorage
{
    /// <summary>No store; callers supply a rendered subject and body.</summary>
    None = 0,

    /// <summary>Files on disk, versioned with the host's source.</summary>
    Files = 1,

    /// <summary>Rows in the host's database, editable at runtime.</summary>
    Database = 2,
}

public class TemplateOptions
{
    public TemplateStorage Storage { get; private set; } = TemplateStorage.None;

    public string Directory { get; private set; } = "EmailTemplates";

    public string Extension { get; private set; } = "scriban";

    public TemplateOptions UseFiles(string directory = "EmailTemplates", string extension = "scriban")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        Storage = TemplateStorage.Files;
        Directory = directory;
        Extension = extension.TrimStart('.');
        return this;
    }

    public TemplateOptions UseDatabase()
    {
        Storage = TemplateStorage.Database;
        return this;
    }
}
