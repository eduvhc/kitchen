namespace MailingKit.Options;

public enum TemplateStorage
{
    /// <summary>No template store is registered; callers supply rendered subject and body.</summary>
    None = 0,

    /// <summary>Templates are files on disk, versioned with the host's source.</summary>
    Files = 1,

    /// <summary>Templates live in the host's database and are editable at runtime.</summary>
    Database = 2,
}

public class TemplateOptions
{
    public TemplateStorage Storage { get; private set; } = TemplateStorage.None;

    /// <summary>Root directory for <see cref="TemplateStorage.Files"/>. Relative paths resolve against the content root.</summary>
    public string Directory { get; private set; } = "EmailTemplates";

    /// <summary>File extension for <see cref="TemplateStorage.Files"/>, without the leading dot.</summary>
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
