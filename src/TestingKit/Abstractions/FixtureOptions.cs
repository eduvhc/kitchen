namespace TestingKit;

public abstract class ContainerOptions
{
    public string? Image { get; set; }

    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>();

    public bool Reuse { get; set; }
}

public abstract class ClientOptions
{
    public string? ConnectionString { get; set; }
}
