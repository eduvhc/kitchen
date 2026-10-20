using MailingKit.Sending;
using ResultingKit;

namespace MailingKit.UnitTests.Sending;

public sealed class FakeEmailSender : IEmailSender
{
    public List<OutgoingEmail> Sent { get; } = [];

    /// <summary>Every call, whatever its outcome.</summary>
    public List<OutgoingEmail> Attempts { get; } = [];

    public Func<OutgoingEmail, Result<SendReceipt>> Behaviour { get; set; } = _ => new SendReceipt("provider-1");

    public Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        Attempts.Add(email);
        var result = Behaviour(email);

        if (result.IsSuccess)
        {
            Sent.Add(email);
        }

        return Task.FromResult(result);
    }
}
