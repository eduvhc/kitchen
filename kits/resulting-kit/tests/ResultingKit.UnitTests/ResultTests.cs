namespace ResultingKit.UnitTests;

[TestClass]
public sealed class ResultTests
{
    private static readonly Error s_missing = Error.NotFound("order.not_found", "Order 42 does not exist.");

    [TestMethod]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsFailure);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void An_error_converts_to_a_failure()
    {
        Result result = s_missing;

        Assert.IsTrue(result.IsFailure);
        Assert.AreSame(s_missing, result.Error);
    }

    [TestMethod]
    public void A_value_and_an_error_both_convert_to_a_typed_result()
    {
        Result<int> ok = 7;
        Result<int> failed = s_missing;

        Assert.AreEqual(7, ok.Value);
        Assert.AreSame(s_missing, failed.Error);
    }

    [TestMethod]
    public void Reading_the_value_of_a_failure_throws_with_the_reason()
    {
        var failed = Result.Failure<int>(s_missing);

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => failed.Value);
        StringAssert.Contains(error.Message, "order.not_found");
    }

    [TestMethod]
    public void Only_transient_errors_are_retryable()
    {
        Assert.IsTrue(Error.Transient("smtp.timeout", "timed out").IsTransient);

        foreach (var kind in Enum.GetValues<ErrorKind>().Where(k => k != ErrorKind.Transient))
        {
            Assert.IsFalse(new Error("x", "x", kind).IsTransient, $"{kind} should be permanent");
        }
    }

    [TestMethod]
    public void Map_and_bind_run_on_success()
    {
        var result = Result.Success(2)
            .Map(x => x * 10)
            .Bind(x => x > 10 ? Result.Success($"#{x}") : Result.Failure<string>(s_missing));

        Assert.AreEqual("#20", result.Value);
    }

    [TestMethod]
    public void Map_and_bind_short_circuit_on_failure()
    {
        var called = false;

        var result = Result.Failure<int>(s_missing)
            .Map(x => { called = true; return x * 10; })
            .Bind(x => { called = true; return Result.Success(x); });

        Assert.IsFalse(called);
        Assert.AreSame(s_missing, result.Error);
    }

    [TestMethod]
    public void Match_picks_the_branch()
    {
        Assert.AreEqual("ok 3", Result.Success(3).Match(v => $"ok {v}", e => e.Code));
        Assert.AreEqual("order.not_found", Result.Failure<int>(s_missing).Match(v => $"ok {v}", e => e.Code));
    }

    [TestMethod]
    public void MapError_rewrites_only_failures()
    {
        var success = Result.Success(1).MapError(_ => Error.Failure("other", "other"));
        var failure = Result.Failure<int>(s_missing).MapError(e => e with { Code = "renamed" });

        Assert.IsTrue(success.IsSuccess);
        Assert.AreEqual("renamed", failure.Error!.Code);
        Assert.AreEqual(ErrorKind.NotFound, failure.Error.Kind);
    }

    [TestMethod]
    public void TryGetValue_and_GetValueOrDefault()
    {
        Assert.IsTrue(Result.Success("a").TryGetValue(out var value));
        Assert.AreEqual("a", value);

        Assert.IsFalse(Result.Failure<string>(s_missing).TryGetValue(out _));
        Assert.AreEqual("fallback", Result.Failure<string>(s_missing).GetValueOrDefault("fallback"));
    }

    [TestMethod]
    public async Task Async_pipeline_chains_and_short_circuits()
    {
        static Task<Result<int>> Load(int id) =>
            Task.FromResult(id == 42 ? Result.Failure<int>(s_missing) : Result.Success(id));

        var ok = await Load(1).BindAsync(x => Load(x + 1)).MapAsync(x => x * 100);
        var failed = await Load(42).BindAsync(x => Load(x + 1)).MapAsync(x => x * 100);

        Assert.AreEqual(200, ok.Value);
        Assert.AreSame(s_missing, failed.Error);
        Assert.AreEqual("missing", await Load(42).MatchAsync(_ => "found", _ => "missing"));
    }
}
