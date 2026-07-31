using MailingKit.Transport;

namespace MailingKit.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    public List<OutgoingEmail> Sent { get; } = [];

    public Func<OutgoingEmail, SendResult> Behaviour { get; set; } = _ => SendResult.Ok("provider-1");

    public Task<SendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var result = Behaviour(email);

        if (result.Success)
        {
            Sent.Add(email);
        }

        return Task.FromResult(result);
    }
}
