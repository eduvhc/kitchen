namespace ResultingKit.UnitTests;

[TestClass]
public sealed class ResultTaskExtensionsTests
{
    private static readonly Error s_missing = Error.NotFound("order.not_found", "Order 42 does not exist.");

    private static Task<Result<int>> LoadAsync(int id) =>
        Task.FromResult(id == 42 ? Result.Failure<int>(s_missing) : Result.Success(id));

    [TestMethod]
    public async Task MapAsync_transforms_a_success()
    {
        Assert.AreEqual(100, (await LoadAsync(1).MapAsync(x => x * 100)).Value);
    }

    [TestMethod]
    public async Task MapAsync_keeps_a_failure()
    {
        Assert.AreSame(s_missing, (await LoadAsync(42).MapAsync(x => x * 100)).Error);
    }

    [TestMethod]
    public async Task BindAsync_chains_from_a_result()
    {
        Assert.AreEqual(2, (await Result.Success(1).BindAsync(x => LoadAsync(x + 1))).Value);
    }

    [TestMethod]
    public async Task BindAsync_skips_next_after_a_failure()
    {
        var called = false;

        var result = await Result.Failure<int>(s_missing).BindAsync(x =>
        {
            called = true;
            return LoadAsync(x);
        });

        Assert.IsFalse(called);
        Assert.AreSame(s_missing, result.Error);
    }

    [TestMethod]
    public async Task BindAsync_chains_from_a_task()
    {
        Assert.AreEqual(2, (await LoadAsync(1).BindAsync(x => LoadAsync(x + 1))).Value);
        Assert.AreSame(s_missing, (await LoadAsync(41).BindAsync(x => LoadAsync(x + 1))).Error);
    }

    [TestMethod]
    public async Task Pipeline_stops_at_the_first_failure()
    {
        var result = await LoadAsync(42)
            .BindAsync(x => LoadAsync(x + 1))
            .MapAsync(x => x * 100);

        Assert.AreSame(s_missing, result.Error);
    }

    [TestMethod]
    public async Task MatchAsync_picks_the_branch()
    {
        Assert.AreEqual("found", await LoadAsync(1).MatchAsync(_ => "found", _ => "missing"));
        Assert.AreEqual("missing", await LoadAsync(42).MatchAsync(_ => "found", _ => "missing"));
    }

    [TestMethod]
    public async Task Extensions_reject_null_arguments()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result<int>>)null!).MapAsync(x => x));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Result<int>)null!).BindAsync(LoadAsync));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Result.Success(1).BindAsync<int, int>(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result<int>>)null!).BindAsync(LoadAsync));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result<int>>)null!).MatchAsync(x => x, _ => 0));
    }
}
