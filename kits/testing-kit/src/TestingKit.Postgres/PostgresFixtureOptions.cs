namespace TestingKit.Postgres;

public sealed class PostgresContainerOptions : ContainerOptions
{
    public const string DefaultImage = "postgres:17-alpine";

    public PostgresContainerOptions() => Image = DefaultImage;

    public string Database { get; set; } = "testdb";

    public string Username { get; set; } = "postgres";

    public string Password { get; set; } = "postgres";

    public IList<string> Commands { get; } = [];
}

public sealed class PostgresClientOptions : ClientOptions
{
    public IList<string> SetupScripts { get; } = [];

    public IList<string> SchemasToInclude { get; } = [];

    public IList<string> TablesToIgnore { get; } = [];
}
