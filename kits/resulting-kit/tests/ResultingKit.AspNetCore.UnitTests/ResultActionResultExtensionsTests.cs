using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ResultingKit.AspNetCore.UnitTests;

[TestClass]
public sealed class ResultActionResultExtensionsTests
{
    private static readonly Error s_missing = Error.NotFound("order.not_found", "Order 42 does not exist.");

    private sealed record Response(int Status, string? ContentType, JsonElement Json);

    private static ErrorActionResult AssertProblem(IActionResult actual, int status)
    {
        var result = Assert.IsInstanceOfType<ErrorActionResult>(actual);

        Assert.AreEqual(status, result.StatusCode);
        return result;
    }

    private static async Task<Response> ExecuteAsync(IActionResult result, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddMvcCore();
        configure?.Invoke(services);
        await using var provider = services.BuildServiceProvider();
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = body;

        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

        using var json = JsonDocument.Parse(body.ToArray());
        return new Response(context.Response.StatusCode, context.Response.ContentType, json.RootElement.Clone());
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
    [DataRow(ErrorKind.Failure, 500, "An error occurred while processing your request.")]
    public async Task Failure_is_a_problem(ErrorKind kind, int status, string title)
    {
        var error = new Error("order.locked", "Order 42 is locked.", kind);

        foreach (var result in new[] { Result.Failure(error).ToActionResult(), Result.Failure<string>(error).ToActionResult() })
        {
            Assert.AreSame(error, AssertProblem(result, status).Error);

            var response = await ExecuteAsync(result);

            Assert.AreEqual(status, response.Status);
            StringAssert.StartsWith(response.ContentType, "application/problem+json;");
            Assert.AreEqual(status, response.Json.GetProperty("status").GetInt32());
            Assert.AreEqual(title, response.Json.GetProperty("title").GetString());
            Assert.AreEqual("Order 42 is locked.", response.Json.GetProperty("detail").GetString());
            Assert.AreEqual("order.locked", response.Json.GetProperty("code").GetString());
            Assert.IsFalse(string.IsNullOrEmpty(response.Json.GetProperty("traceId").GetString()));
            StringAssert.StartsWith(response.Json.GetProperty("type").GetString(), "https://tools.ietf.org/html/rfc9110#");
        }
    }

    [TestMethod]
    public async Task Failure_honours_the_host_problem_details_options()
    {
        var response = await ExecuteAsync(Result.Failure(s_missing).ToActionResult(), services =>
        {
            services.Configure<ApiBehaviorOptions>(o => o.ClientErrorMapping[404].Link = "https://example.com/missing");
            services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["node"] = "a");
        });

        Assert.AreEqual("https://example.com/missing", response.Json.GetProperty("type").GetString());
        Assert.AreEqual("a", response.Json.GetProperty("node").GetString());
        Assert.AreEqual("order.not_found", response.Json.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task Failure_for_a_status_without_a_mapping_has_no_title()
    {
        var response = await ExecuteAsync(Result.Failure(s_missing).ToActionResult(_ => 418));

        Assert.AreEqual(418, response.Status);
        Assert.IsFalse(response.Json.TryGetProperty("title", out _));
        Assert.AreEqual("Order 42 does not exist.", response.Json.GetProperty("detail").GetString());
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
        Assert.ThrowsExactly<ArgumentNullException>(() => new ErrorActionResult(null!, 400));
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Result)null!).ToActionResult());
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Result<int>)null!).ToActionResult());
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Success().ToActionResult((Func<IActionResult>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Success(1).ToActionResult((Func<int, IActionResult>)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result>)null!).ToActionResultAsync());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result<int>>)null!).ToActionResultAsync());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Task.FromResult(Result.Success()).ToActionResultAsync((Func<IActionResult>)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Task.FromResult(Result.Success(1)).ToActionResultAsync((Func<int, IActionResult>)null!));
    }

    private static readonly Error s_invalid = Error.Validation(
        "order.invalid",
        "name: Must not be blank. items[0].sku: Is required.",
        new Dictionary<string, string[]> { ["name"] = ["Must not be blank."], ["items[0].sku"] = ["Is required.", "Must be 8 characters long."] });

    private static readonly Error s_taken = Error.Conflict("user.email_taken", "email: Is taken.") with
    {
        FieldErrors = new Dictionary<string, string[]> { ["email"] = ["Is taken."] },
    };

    private static void AssertFieldErrors(JsonElement json)
    {
        var errors = json.GetProperty("errors");
        Assert.AreEqual(2, errors.EnumerateObject().Count());
        Assert.AreEqual("Must not be blank.", errors.GetProperty("name")[0].GetString());
        Assert.AreEqual("Is required.", errors.GetProperty("items[0].sku")[0].GetString());
        Assert.AreEqual("Must be 8 characters long.", errors.GetProperty("items[0].sku")[1].GetString());
    }

    [TestMethod]
    public async Task Failure_with_field_errors_is_a_validation_problem()
    {
        foreach (var result in new[] { Result.Failure(s_invalid).ToActionResult(), Result.Failure<string>(s_invalid).ToActionResult() })
        {
            Assert.AreSame(s_invalid, AssertProblem(result, 400).Error);

            var response = await ExecuteAsync(result);

            Assert.AreEqual(400, response.Status);
            StringAssert.StartsWith(response.ContentType, "application/problem+json;");
            Assert.AreEqual(400, response.Json.GetProperty("status").GetInt32());
            Assert.AreEqual("One or more validation errors occurred.", response.Json.GetProperty("title").GetString());
            Assert.AreEqual("https://tools.ietf.org/html/rfc9110#section-15.5.1", response.Json.GetProperty("type").GetString());
            Assert.AreEqual(s_invalid.Message, response.Json.GetProperty("detail").GetString());
            Assert.AreEqual("order.invalid", response.Json.GetProperty("code").GetString());
            Assert.IsFalse(string.IsNullOrEmpty(response.Json.GetProperty("traceId").GetString()));
            AssertFieldErrors(response.Json);
        }
    }

    [TestMethod]
    public async Task Validation_problem_honours_the_host_problem_details_options()
    {
        var response = await ExecuteAsync(Result.Failure(s_invalid).ToActionResult(), services =>
            services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["node"] = "a"));

        Assert.AreEqual("a", response.Json.GetProperty("node").GetString());
        Assert.AreEqual("order.invalid", response.Json.GetProperty("code").GetString());
        AssertFieldErrors(response.Json);
    }

    [TestMethod]
    public async Task Validation_problem_takes_the_status_override()
    {
        var response = await ExecuteAsync(Result.Failure(s_invalid).ToActionResult(_ => 422));

        Assert.AreEqual(422, response.Status);
        Assert.AreEqual(422, response.Json.GetProperty("status").GetInt32());
        Assert.AreEqual("One or more validation errors occurred.", response.Json.GetProperty("title").GetString());
        Assert.AreEqual("order.invalid", response.Json.GetProperty("code").GetString());
        AssertFieldErrors(response.Json);
    }

    [TestMethod]
    public async Task Field_errors_on_another_kind_keep_its_status()
    {
        var response = await ExecuteAsync(Result.Failure(s_taken).ToActionResult());

        Assert.AreEqual(409, response.Status);
        Assert.AreEqual("user.email_taken", response.Json.GetProperty("code").GetString());
        Assert.AreEqual("Is taken.", response.Json.GetProperty("errors").GetProperty("email")[0].GetString());
    }
}
