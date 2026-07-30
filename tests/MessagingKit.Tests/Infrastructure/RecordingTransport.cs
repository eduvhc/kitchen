using System.Collections.Concurrent;

namespace MessagingKit.Tests.Infrastructure;

public sealed class RecordingTransport : IMessageTransport
{
    public ConcurrentBag<MessageEnvelope> Sent { get; } = [];

    public Func<MessageEnvelope, TransportResult> Behaviour { get; set; } = _ => TransportResult.Ok();

    public Task<TransportResult> SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        var result = Behaviour(envelope);

        if (result.Success)
        {
            Sent.Add(envelope);
        }

        return Task.FromResult(result);
    }

    public void Reset()
    {
        Sent.Clear();
        Behaviour = _ => TransportResult.Ok();
    }
}
