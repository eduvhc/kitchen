namespace ResultingKit.UnitTests;

[TestClass]
public sealed class ResultTests
{
    private static readonly Error s_error = Error.Conflict("order.locked", "Order is locked.");

    [TestMethod]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsFailure);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Success_is_shared()
    {
        Assert.AreSame(Result.Success(), Result.Success());
    }

    [TestMethod]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(s_error);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.IsFailure);
        Assert.AreSame(s_error, result.Error);
    }

    [TestMethod]
    public void Failure_rejects_null()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Failure(null!));
    }

    [TestMethod]
    public void Error_converts_implicitly()
    {
        Result result = s_error;

        Assert.AreSame(s_error, result.Error);
    }

    [TestMethod]
    public void Match_picks_the_branch()
    {
        Assert.AreEqual("ok", Result.Success().Match(() => "ok", e => e.Code));
        Assert.AreEqual("order.locked", Result.Failure(s_error).Match(() => "ok", e => e.Code));
    }

    [TestMethod]
    public void Map_produces_a_value_on_success()
    {
        Assert.AreEqual(5, Result.Success().Map(() => 5).Value);
    }

    [TestMethod]
    public void Map_skips_on_failure()
    {
        var called = false;

        var result = Result.Failure(s_error).Map(() => { called = true; return 5; });

        Assert.IsFalse(called);
        Assert.AreSame(s_error, result.Error);
    }

    [TestMethod]
    public void Bind_runs_next_on_success()
    {
        Assert.AreSame(s_error, Result.Success().Bind(() => s_error).Error);
    }

    [TestMethod]
    public void Bind_returns_the_same_failure()
    {
        var failure = Result.Failure(s_error);

        Assert.AreSame(failure, failure.Bind(Result.Success));
    }

    [TestMethod]
    public void MapError_rewrites_only_failures()
    {
        var success = Result.Success();

        Assert.AreSame(success, success.MapError(_ => Error.Failure("x", "x")));
        Assert.AreEqual("renamed", Result.Failure(s_error).MapError(e => e with { Code = "renamed" }).Error!.Code);
    }

    [TestMethod]
    public void Combinators_reject_null_delegates()
    {
        var result = Result.Success();

        Assert.ThrowsExactly<ArgumentNullException>(() => result.Match(null!, e => 0));
        Assert.ThrowsExactly<ArgumentNullException>(() => result.Match(() => 0, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => result.Map<int>(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => result.Bind(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => result.MapError(null!));
    }

    [TestMethod]
    public void ToString_describes_the_outcome()
    {
        Assert.AreEqual("Success", Result.Success().ToString());
        Assert.AreEqual("Failure(order.locked: Order is locked.)", Result.Failure(s_error).ToString());
    }
}
