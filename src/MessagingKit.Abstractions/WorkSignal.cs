namespace MessagingKit;

/// <summary>
/// Lets a background loop wake as soon as there is work instead of waiting out its poll interval.
/// The interval becomes the fallback — a missed signal costs latency, never correctness.
/// </summary>
public abstract class WorkSignal : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);
    private bool _disposed;

    /// <summary>
    /// Marks work available. Coalescing is deliberate: several rows written together wake the loop
    /// once, and it drains whatever it finds.
    /// </summary>
    public void Pulse()
    {
        if (_disposed || _semaphore.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Raced with another pulse; the loop is already about to run.
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    /// <summary>Waits for a pulse, giving up after <paramref name="timeout"/>.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) =>
        _semaphore.WaitAsync(timeout, ct);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
