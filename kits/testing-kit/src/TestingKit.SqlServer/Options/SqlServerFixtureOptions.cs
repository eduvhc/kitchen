namespace TestingKit.SqlServer;

public sealed class SqlServerContainerOptions : ContainerOptions
{
    public const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest";

    public SqlServerContainerOptions() => Image = DefaultImage;

    public string Password { get; set; } = "Strong_password_123!";
}

public sealed class SqlServerClientOptions : ClientOptions
{
    public IList<string> SetupScripts { get; } = [];

    public IList<string> SchemasToInclude { get; } = [];

    public IList<string> TablesToIgnore { get; } = [];
}
