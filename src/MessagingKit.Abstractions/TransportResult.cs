namespace MessagingKit;

public readonly record struct TransportResult(bool Success, string? Error = null, bool IsPermanent = false)
{
    public static TransportResult Ok() => new(true);

    public static TransportResult Transient(string error) => new(false, error);

    public static TransportResult Permanent(string error) => new(false, error, IsPermanent: true);
}
