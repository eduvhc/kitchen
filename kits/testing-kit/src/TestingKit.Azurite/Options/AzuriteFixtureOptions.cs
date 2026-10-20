namespace TestingKit.Azurite;

public sealed class AzuriteContainerOptions : ContainerOptions
{
    public const string DefaultImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    public AzuriteContainerOptions() => Image = DefaultImage;

    public bool SkipApiVersionCheck { get; set; } = true;
}

public sealed class AzuriteClientOptions : ClientOptions
{
    public IList<string> ContainersToReset { get; } = [];
}
