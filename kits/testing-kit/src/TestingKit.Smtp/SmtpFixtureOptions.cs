namespace TestingKit.Smtp;

public sealed class SmtpContainerOptions : ContainerOptions
{
    public const string DefaultImage = "axllent/mailpit:v1.27";

    public SmtpContainerOptions() => Image = DefaultImage;

    public int SmtpPort { get; set; } = 1025;

    public int ApiPort { get; set; } = 8025;
}

public sealed class SmtpClientOptions : ClientOptions
{
    public string? ApiBaseAddress { get; set; }

    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);
}
