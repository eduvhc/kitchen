using System.Diagnostics;

namespace LockingKit.IntegrationTests;

/// <summary>
/// Records every critical section as an interval and fails if any two overlap. A section starts after acquire returns
/// and ends before the handle is disposed, so with a correct lock the next one cannot start before the last ended.
/// </summary>
internal sealed class ExclusionChecker
{
    private readonly List<(long Start, long End, string Who)> _intervals = [];
    private int _inside;
    private int _overlaps;

    public int Count
    {
        get
        {
            lock (_intervals)
            {
                return _intervals.Count;
            }
        }
    }

    public async Task RunAsync(string who, Func<Task> work)
    {
        var start = Stopwatch.GetTimestamp();

        if (Interlocked.Increment(ref _inside) != 1)
        {
            Interlocked.Increment(ref _overlaps);
        }

        try
        {
            await work();
        }
        finally
        {
            Interlocked.Decrement(ref _inside);
            var end = Stopwatch.GetTimestamp();

            lock (_intervals)
            {
                _intervals.Add((start, end, who));
            }
        }
    }

    public void AssertExclusive(int expected)
    {
        Assert.AreEqual(0, Volatile.Read(ref _overlaps), "Two holders were inside the critical section at once.");

        lock (_intervals)
        {
            Assert.HasCount(expected, _intervals);

            var ordered = _intervals.OrderBy(i => i.Start).ToList();

            for (var i = 1; i < ordered.Count; i++)
            {
                Assert.IsGreaterThanOrEqualTo(
                    ordered[i - 1].End,
                    ordered[i].Start,
                    $"{ordered[i].Who} started before {ordered[i - 1].Who} ended.");
            }
        }
    }
}

/// <summary>A resource that enforces fencing: it refuses writes carrying a token lower than one it has seen.</summary>
internal sealed class FencedResource
{
    private long _highest = long.MinValue;

    public List<string> Writes { get; } = [];

    public bool TryWrite(long token, string value)
    {
        lock (Writes)
        {
            if (token < _highest)
            {
                return false;
            }

            _highest = token;
            Writes.Add(value);
            return true;
        }
    }
}
