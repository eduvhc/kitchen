namespace LockingKit;

/// <summary>
/// Orders the waiters of one process. Per key, only the waiter at the front of the line polls the backend; the rest
/// queue behind it in arrival order, so local waiters are served first come, first served and a process sends one
/// stream of attempts per key however many of its callers wait. When a handle of this process releases a key, the
/// front waiter is woken at once instead of finishing its backoff pause.
/// </summary>
internal sealed class WaitingRoom(TimeProvider timeProvider)
{
    private readonly Dictionary<string, Line> _lines = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>Waits to reach the front of the line for <paramref name="key"/>.</summary>
    /// <returns>The turn, to dispose when done; or <see langword="null"/> when <paramref name="timeout"/> ran out first.</returns>
    public async Task<Turn?> WaitForTurnAsync(string key, TimeSpan timeout, CancellationToken ct)
    {
        Line line;
        LinkedListNode<TaskCompletionSource<bool>> node;

        lock (_lines)
        {
            if (!_lines.TryGetValue(key, out line!))
            {
                line = new Line();
                _lines.Add(key, line);
            }

            line.Users++;

            if (!line.Occupied)
            {
                line.Occupied = true;
                return new Turn(this, key, line);
            }

            node = line.Waiters.AddLast(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        var waiter = node.Value;
        using var timer = timeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(timeout, _timeProvider);
        using var onTimeout = timer?.Token.Register(() => waiter.TrySetResult(false));
        using var onCancel = ct.Register(() => waiter.TrySetCanceled(ct));

        bool granted;

        try
        {
            granted = await waiter.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Leave(key, line, node);
            throw;
        }

        if (!granted)
        {
            Leave(key, line, node);
            return null;
        }

        return new Turn(this, key, line);
    }

    /// <summary>Wakes the front waiter for <paramref name="key"/>, if any, because the lock may now be free.</summary>
    public void Pulse(string key)
    {
        TaskCompletionSource? released = null;

        lock (_lines)
        {
            if (_lines.TryGetValue(key, out var line))
            {
                released = line.Released;
                line.Released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        released?.TrySetResult();
    }

    internal int LineCount
    {
        get
        {
            lock (_lines)
            {
                return _lines.Count;
            }
        }
    }

    private void Leave(string key, Line line, LinkedListNode<TaskCompletionSource<bool>> node)
    {
        lock (_lines)
        {
            if (node.List is not null)
            {
                // Never granted: step out of the line.
                line.Waiters.Remove(node);
            }
            else
            {
                // Granted at the same moment it timed out or was cancelled: pass the turn on.
                GrantNext(line);
            }

            Depart(key, line);
        }
    }

    private void EndTurn(string key, Line line)
    {
        lock (_lines)
        {
            GrantNext(line);
            Depart(key, line);
        }
    }

    private static void GrantNext(Line line)
    {
        while (line.Waiters.First is { } next)
        {
            line.Waiters.RemoveFirst();

            // A waiter that already timed out or was cancelled refuses the turn; offer it to the one behind.
            if (next.Value.TrySetResult(true))
            {
                return;
            }
        }

        line.Occupied = false;
    }

    private void Depart(string key, Line line)
    {
        if (--line.Users == 0)
        {
            _lines.Remove(key);
        }
    }

    internal sealed class Line
    {
        public LinkedList<TaskCompletionSource<bool>> Waiters { get; } = new();

        public bool Occupied { get; set; }

        public int Users { get; set; }

        public TaskCompletionSource Released { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>The front of the line for one key.</summary>
    internal sealed class Turn : IDisposable
    {
        private readonly WaitingRoom _room;
        private readonly string _key;
        private readonly Line _line;
        private int _ended;

        public Turn(WaitingRoom room, string key, Line line)
        {
            _room = room;
            _key = key;
            _line = line;
        }

        /// <summary>
        /// The signal a local release completes. Take it before an attempt, so a release that lands between the attempt
        /// and the pause still cuts the pause short.
        /// </summary>
        public Task ReleaseSignal()
        {
            lock (_room._lines)
            {
                return _line.Released.Task;
            }
        }

        /// <summary>Pauses for <paramref name="delay"/>, or less when <paramref name="released"/> completes.</summary>
        public async Task PauseAsync(TimeSpan delay, Task released, CancellationToken ct)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sleep = Task.Delay(delay, _room._timeProvider, stop.Token);

            await Task.WhenAny(sleep, released).ConfigureAwait(false);
            stop.Cancel();
            ct.ThrowIfCancellationRequested();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                _room.EndTurn(_key, _line);
            }
        }
    }
}
