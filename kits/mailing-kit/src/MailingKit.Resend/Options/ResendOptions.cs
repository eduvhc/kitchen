namespace MailingKit.Resend;

/// <summary>Settings for <see cref="ResendEmailSender"/>. Bound from the <c>Resend</c> configuration section.</summary>
public class ResendOptions
{
    /// <summary>The configuration section <c>AddResendTransport(configuration)</c> binds: <c>Resend</c>.</summary>
    public const string SectionName = "Resend";

    /// <summary>
    /// The Resend API key, <c>re_…</c>. Required. Keep it in a secret store or an environment variable
    /// (<c>Resend__ApiKey</c>), never in <c>appsettings.json</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The API root. Defaults to <c>https://api.resend.com/</c>; change it only for a proxy or a stub.</summary>
    public Uri BaseUrl { get; set; } = new("https://api.resend.com/");

    /// <summary>How long one request may take before it is abandoned and reported as transient. Defaults to 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
