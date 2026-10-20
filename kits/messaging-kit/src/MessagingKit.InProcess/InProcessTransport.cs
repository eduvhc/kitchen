using MessagingKit.Abstractions;
using MessagingKit.Inbox;
using Microsoft.Extensions.Logging;
using ResultingKit;

namespace MessagingKit.InProcess;

/// <summary>
/// Hands the envelope to the inbox in the same process. Modules talk through the same
/// outbox → transport → inbox seam they would use across a broker, so extracting one into its own
/// deployment later swaps this registration and touches no module code.
/// </summary>
/// <remarks>
/// Storing to the inbox and marking the outbox row sent are two writes, not one transaction. A crash
/// between them leaves the outbox row pending, it is redelivered, and the inbox rejects the duplicate
/// on its primary key. That gap is exactly what the inbox is for.
/// </remarks>
public sealed class InProcessTransport(
    IInbox inbox,
    InboxSignal signal,
    ILogger<InProcessTransport> logger) : IMessageTransport
{
    public async Task<Result> SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            var stored = await inbox.TryStoreAsync(envelope, ct);

            if (stored)
            {
                // Wake the processor rather than let the message sit out the poll interval.
                signal.Pulse();
            }
            else if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Inbox already holds {MessageId}; delivery is a no-op", envelope.Id);
            }

            // A duplicate is a successful delivery — the receiver already has it.
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Writing to the inbox failed, so the outbox row should be retried.
            return Error.Transient("inprocess.inbox_unavailable", $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
