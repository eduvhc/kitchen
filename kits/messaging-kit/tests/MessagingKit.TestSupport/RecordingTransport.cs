using System.Collections.Concurrent;
using MessagingKit.Abstractions;
using ResultingKit;

namespace MessagingKit.TestSupport;

public sealed class RecordingTransport : IMessageTransport
{
    public ConcurrentBag<MessageEnvelope> Sent { get; } = [];

    public Func<MessageEnvelope, Result> Behaviour { get; set; } = _ => Result.Success();

    public Task<Result> SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        var result = Behaviour(envelope);

        if (result.IsSuccess)
        {
            Sent.Add(envelope);
        }

        return Task.FromResult(result);
    }

    public void Reset()
    {
        Sent.Clear();
        Behaviour = _ => Result.Success();
    }
}
