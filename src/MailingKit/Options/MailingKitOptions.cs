namespace MailingKit.Options;

public class MailingKitOptions
{
    private string _schema = "email";

    /// <summary>
    /// Postgres schema holding the emails (and, when enabled, templates) tables.
    /// Emitted into raw SQL as an identifier, so it is validated rather than parameterised.
    /// </summary>
    public string Schema
    {
        get => _schema;
        set => _schema = ValidateSchema(value);
    }

    public EmailDefaultsOptions Defaults { get; } = new();

    public DispatcherOptions Dispatcher { get; } = new();

    public TemplateOptions Templates { get; } = new();

    private static string ValidateSchema(string value)
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
