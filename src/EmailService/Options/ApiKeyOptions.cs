namespace EmailService.Options;

public class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    public bool Enabled { get; set; } = true;
    public string HeaderName { get; set; } = "X-Api-Key";
    public Dictionary<string, ApiKeyEntry> Keys { get; set; } = [];
}

public class ApiKeyEntry
{
    public required string Key { get; set; }
    public bool IsAdmin { get; set; }
}
