using System.Collections.Concurrent;

namespace MessagingKit.TestSupport;

public sealed class HandlerLog
{
    public ConcurrentBag<(SendEmail Message, MessageContext Context)> Handled { get; } = [];

    public Func<SendEmail, Task>? Behaviour { get; set; }

    public void Reset()
    {
        Handled.Clear();
        Behaviour = null;
    }
}

public sealed class RecordingHandler(HandlerLog log) : IMessageHandler<SendEmail>
{
    public async Task HandleAsync(SendEmail message, MessageContext context, CancellationToken ct = default)
    {
        if (log.Behaviour is not null)
        {
            await log.Behaviour(message);
        }

        log.Handled.Add((message, context));
    }
}
