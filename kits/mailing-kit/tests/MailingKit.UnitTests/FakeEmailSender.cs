using MailingKit.Transport;
using ResultingKit;

namespace MailingKit.UnitTests;

public sealed class FakeEmailSender : IEmailSender
{
    public List<OutgoingEmail> Sent { get; } = [];

    public Func<OutgoingEmail, Result<SendReceipt>> Behaviour { get; set; } = _ => new SendReceipt("provider-1");

    public Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var result = Behaviour(email);

        if (result.IsSuccess)
        {
            Sent.Add(email);
        }

        return Task.FromResult(result);
    }
}
