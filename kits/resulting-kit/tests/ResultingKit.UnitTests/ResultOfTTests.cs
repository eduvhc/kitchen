namespace ResultingKit.UnitTests;

[TestClass]
public sealed class ResultOfTTests
{
    private static readonly Error s_missing = Error.NotFound("order.not_found", "Order 42 does not exist.");

    [TestMethod]
    public void Success_carries_the_value()
    {
        var result = Result.Success(7);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(7, result.Value);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Success_allows_a_null_value()
    {
        var result = Result.Success<string?>(null);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public void Value_and_error_convert_implicitly()
    {
        Result<int> ok = 7;
        Result<int> failed = s_missing;

        Assert.AreEqual(7, ok.Value);
        Assert.AreSame(s_missing, failed.Error);
    }

    [TestMethod]
    public void Is_usable_as_a_plain_result()
    {
        Result result = Result.Failure<int>(s_missing);

        Assert.IsTrue(result.IsFailure);
        Assert.AreSame(s_missing, result.Error);
    }

    [TestMethod]
    public void Failure_rejects_null()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Failure<int>(null!));
    }

    [TestMethod]
    public void Value_of_a_failure_throws_with_the_error()
    {
        var failed = Result.Failure<int>(s_missing);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => failed.Value);
        StringAssert.Contains(exception.Message, "order.not_found");
    }

    [TestMethod]
    public void TryGetValue_reports_success()
    {
        Assert.IsTrue(Result.Success("a").TryGetValue(out var value));
        Assert.AreEqual("a", value);
        Assert.IsFalse(Result.Failure<string>(s_missing).TryGetValue(out _));
    }

    [TestMethod]
    public void GetValueOrDefault_falls_back_on_failure()
    {
        Assert.AreEqual("a", Result.Success("a").GetValueOrDefault("fallback"));
        Assert.AreEqual("fallback", Result.Failure<string>(s_missing).GetValueOrDefault("fallback"));
    }

    [TestMethod]
    public void Match_picks_the_branch()
    {
        Assert.AreEqual("ok 3", Result.Success(3).Match(v => $"ok {v}", e => e.Code));
        Assert.AreEqual("order.not_found", Result.Failure<int>(s_missing).Match(v => $"ok {v}", e => e.Code));
    }

    [TestMethod]
    public void Map_and_bind_chain_on_success()
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
    public void Bind_can_end_in_a_plain_result()
    {
        Assert.IsTrue(Result.Success(1).Bind(_ => Result.Success()).IsSuccess);
        Assert.AreSame(s_missing, Result.Success(1).Bind(_ => Result.Failure(s_missing)).Error);
    }

    [TestMethod]
    public void Bind_to_a_plain_result_keeps_the_original_failure()
    {
        var failure = Result.Failure<int>(s_missing);

        Assert.AreSame(failure, failure.Bind(_ => Result.Success()));
    }

    [TestMethod]
    public void MapError_rewrites_only_failures()
    {
        var success = Result.Success(1);
        var failure = Result.Failure<int>(s_missing).MapError(e => e with { Code = "renamed" });

        Assert.AreSame(success, success.MapError(_ => Error.Failure("x", "x")));
        Assert.AreEqual("renamed", failure.Error!.Code);
        Assert.AreEqual(ErrorKind.NotFound, failure.Error.Kind);
    }

    [TestMethod]
    public void Combinators_reject_null_delegates()
    {
        var result = Result.Success(1);

        Assert.ThrowsExactly<ArgumentNullException>(() => result.Match<int>(null!, e => 0));
        Assert.ThrowsExactly<ArgumentNullException>(() => result.Map<int>(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => result.Bind<int>(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => result.MapError(null!));
    }

    [TestMethod]
    public void ToString_describes_the_outcome()
    {
        Assert.AreEqual("Success(7)", Result.Success(7).ToString());
        Assert.AreEqual("Failure(order.not_found: Order 42 does not exist.)", Result.Failure<int>(s_missing).ToString());
    }
}
