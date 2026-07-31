namespace MailingKit.Options;

public class MailingOptions
{
    private string _schema = "email";

    /// <summary>Schema holding the send log, and the templates table when they live in the database.</summary>
    public string Schema
    {
        get => _schema;
        set => _schema = Validate(value);
    }

    public EmailDefaultsOptions Defaults { get; } = new();

    public TemplateOptions Templates { get; } = new();

    private static string Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Schema cannot be empty.", nameof(value));
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException(
                    $"Schema '{value}' may only contain ASCII letters, digits, and underscores.",
                    nameof(value));
            }
        }

        return value;
    }
}
