using Microsoft.AspNetCore.Mvc;

namespace ResultingKit.AspNetCore.UnitTests;

[TestClass]
public sealed class ResultActionResultExtensionsTests
{
    private static readonly Error s_missing = Error.NotFound("order.not_found", "Order 42 does not exist.");

    private static ProblemDetails AssertProblem(IActionResult actual, int status)
    {
        var result = Assert.IsInstanceOfType<ObjectResult>(actual);
        var problem = Assert.IsInstanceOfType<ProblemDetails>(result.Value);

        Assert.AreEqual(status, result.StatusCode);
        Assert.AreEqual(status, problem.Status);
        CollectionAssert.AreEqual(new[] { "application/problem+json" }, result.ContentTypes.ToArray());
        return problem;
    }

    [TestMethod]
    public void Success_is_204()
    {
        Assert.IsInstanceOfType<NoContentResult>(Result.Success().ToActionResult());
    }

    [TestMethod]
    public void Success_with_a_value_is_200_with_the_value()
    {
        var ok = Assert.IsInstanceOfType<OkObjectResult>(Result.Success("tea").ToActionResult());

        Assert.AreEqual(200, ok.StatusCode);
        Assert.AreEqual("tea", ok.Value);
    }

    [TestMethod]
    [DataRow(ErrorKind.Validation, 400, "Bad Request")]
    [DataRow(ErrorKind.Unauthorized, 401, "Unauthorized")]
    [DataRow(ErrorKind.Forbidden, 403, "Forbidden")]
    [DataRow(ErrorKind.NotFound, 404, "Not Found")]
    [DataRow(ErrorKind.Conflict, 409, "Conflict")]
    [DataRow(ErrorKind.Transient, 503, "Service Unavailable")]
    [DataRow(ErrorKind.Failure, 500, "Internal Server Error")]
    public void Failure_is_a_problem(ErrorKind kind, int status, string title)
    {
        var error = new Error("order.locked", "Order 42 is locked.", kind);

        foreach (var result in new[] { Result.Failure(error).ToActionResult(), Result.Failure<string>(error).ToActionResult() })
        {
            var problem = AssertProblem(result, status);

            Assert.AreEqual(title, problem.Title);
            Assert.AreEqual("Order 42 is locked.", problem.Detail);
            Assert.AreEqual("order.locked", problem.Extensions["code"]);
            StringAssert.StartsWith(problem.Type, "https://tools.ietf.org/html/rfc9110#");
        }
    }

    [TestMethod]
    public void Success_override_builds_the_response()
    {
        var created = Assert.IsInstanceOfType<CreatedResult>(
            Result.Success(7).ToActionResult(id => new CreatedResult($"/orders/{id}", id)));

        Assert.AreEqual("/orders/7", created.Location);
        Assert.IsInstanceOfType<AcceptedResult>(Result.Success().ToActionResult(() => new AcceptedResult()));
    }

    [TestMethod]
    public void Success_override_is_not_called_on_failure()
    {
        var called = false;

        var result = Result.Failure<int>(s_missing).ToActionResult(v =>
        {
            called = true;
            return new OkObjectResult(v);
        });

        Assert.IsFalse(called);
        AssertProblem(result, 404);
    }

    [TestMethod]
    public void Status_override_picks_the_status()
    {
        static int Map(Error e) => e.Code == "order.invalid" ? 422 : e.ToStatusCode();

        AssertProblem(Result.Failure(Error.Validation("order.invalid", "Bad.")).ToActionResult(Map), 422);
        AssertProblem(Result.Failure<int>(Error.Validation("order.empty", "Empty.")).ToActionResult(statusCode: Map), 400);
        AssertProblem(Result.Failure(s_missing).ToActionResult(() => new AcceptedResult(), _ => 418), 418);
        AssertProblem(Result.Failure<int>(s_missing).ToActionResult(v => new OkObjectResult(v), _ => 418), 418);
    }

    [TestMethod]
    public async Task Task_overloads_convert_after_awaiting()
    {
        Assert.IsInstanceOfType<NoContentResult>(await Task.FromResult(Result.Success()).ToActionResultAsync());
        Assert.IsInstanceOfType<AcceptedResult>(await Task.FromResult(Result.Success()).ToActionResultAsync(() => new AcceptedResult()));
        Assert.IsInstanceOfType<OkObjectResult>(await Task.FromResult(Result.Success(1)).ToActionResultAsync());
        Assert.IsInstanceOfType<CreatedResult>(await Task.FromResult(Result.Success(1)).ToActionResultAsync(v => new CreatedResult("/x", v)));
        AssertProblem(await Task.FromResult(Result.Failure(s_missing)).ToActionResultAsync(), 404);
        AssertProblem(await Task.FromResult(Result.Failure<int>(s_missing)).ToActionResultAsync(_ => 418), 418);
    }

    [TestMethod]
    public async Task Null_arguments_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Result)null!).ToActionResult());
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Result<int>)null!).ToActionResult());
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Success().ToActionResult((Func<IActionResult>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Success(1).ToActionResult((Func<int, IActionResult>)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result>)null!).ToActionResultAsync());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result<int>>)null!).ToActionResultAsync());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Task.FromResult(Result.Success()).ToActionResultAsync((Func<IActionResult>)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Task.FromResult(Result.Success(1)).ToActionResultAsync((Func<int, IActionResult>)null!));
    }
}
