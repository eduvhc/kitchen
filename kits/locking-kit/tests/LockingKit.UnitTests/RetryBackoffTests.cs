namespace LockingKit.UnitTests;

[TestClass]
public sealed class RetryBackoffTests
{
    private static readonly TimeSpan Min = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(1);

    [TestMethod]
    [DataRow(0, 50)]
    [DataRow(1, 100)]
    [DataRow(2, 200)]
    [DataRow(3, 400)]
    [DataRow(4, 800)]
    [DataRow(5, 1000)]
    [DataRow(40, 1000)]
    [DataRow(62, 1000)]
    [DataRow(int.MaxValue, 1000)]
    public void The_ceiling_doubles_from_the_minimum_and_stops_at_the_cap(int retry, int ceilingMs)
    {
        var top = new RetryBackoff(Min, Max, () => 1.0).Next(retry);
        var bottom = new RetryBackoff(Min, Max, () => 0.0).Next(retry);

        Assert.AreEqual(TimeSpan.FromMilliseconds(ceilingMs), top);
        Assert.AreEqual(TimeSpan.FromMilliseconds(ceilingMs) / 2, bottom, "Equal jitter: never below half the ceiling.");
    }

    [TestMethod]
    public void Jitter_spreads_pauses_over_the_upper_half_of_the_ceiling()
    {
        var backoff = new RetryBackoff(Min, Max);
        var samples = Enumerable.Range(0, 2000).Select(_ => backoff.Next(10).TotalMilliseconds).ToArray();

        Assert.IsTrue(samples.All(s => s is >= 500 and <= 1000), "Every pause within [cap / 2, cap].");
        Assert.IsGreaterThan(50, samples.Distinct().Count(), "Contenders do not retry in lockstep.");
        Assert.IsLessThan(650, samples.Min() + 100);
        Assert.IsGreaterThan(850, samples.Max());
    }

    [TestMethod]
    public void A_long_waiter_polls_as_often_as_a_new_one_once_both_reach_the_cap()
    {
        var backoff = new RetryBackoff(Min, Max, () => 0.5);

        Assert.AreEqual(backoff.Next(6), backoff.Next(600), "No exponential starvation of old waiters.");
    }

    [TestMethod]
    public void Out_of_range_random_values_are_clamped()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(1), new RetryBackoff(Min, Max, () => 7).Next(20));
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), new RetryBackoff(Min, Max, () => -3).Next(20));
    }

    [TestMethod]
    public void Bad_bounds_and_negative_retries_are_bugs()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RetryBackoff(TimeSpan.Zero, Max));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RetryBackoff(Max, Min));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RetryBackoff(Min, Max).Next(-1));
    }
}
