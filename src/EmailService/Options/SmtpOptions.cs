using MailKit.Security;

namespace EmailService.Options;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public bool AcceptAllCertificates { get; set; }
}
