using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ResultingKit.AspNetCore.UnitTests;

[TestClass]
public sealed class ResultHttpResultExtensionsTests
{
    private static readonly Error s_missing = Error.NotFound("order.not_found", "Order 42 does not exist.");

    private sealed record Order(int Id, string Name);

    private sealed record Response(int Status, string? ContentType, string? Location, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement;
    }

    private static async Task<Response> ExecuteAsync(IResult result, Action<IServiceCollection>? configure = null)
    {
        var collection = new ServiceCollection().AddLogging();
        configure?.Invoke(collection);
        await using var services = collection.BuildServiceProvider();
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        return new Response(
            context.Response.StatusCode,
            context.Response.ContentType,
            context.Response.Headers.Location,
            System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    [TestMethod]
    public async Task Success_is_204()
    {
        var response = await ExecuteAsync(Result.Success().ToHttpResult());

        Assert.AreEqual(204, response.Status);
        Assert.AreEqual("", response.Body);
    }

    [TestMethod]
    public async Task Success_with_a_value_is_200_with_the_value()
    {
        var response = await ExecuteAsync(Result.Success(new Order(7, "Tea")).ToHttpResult());

        Assert.AreEqual(200, response.Status);
        StringAssert.StartsWith(response.ContentType, "application/json");
        Assert.AreEqual(7, response.Json.GetProperty("id").GetInt32());
        Assert.AreEqual("Tea", response.Json.GetProperty("name").GetString());
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

        foreach (var result in new[] { Result.Failure(error).ToHttpResult(), Result.Failure<Order>(error).ToHttpResult() })
        {
            var response = await ExecuteAsync(result);

            Assert.AreEqual(status, response.Status);
            Assert.AreEqual("application/problem+json", response.ContentType);
            Assert.AreEqual(status, response.Json.GetProperty("status").GetInt32());
            Assert.AreEqual(title, response.Json.GetProperty("title").GetString());
            Assert.AreEqual("Order 42 is locked.", response.Json.GetProperty("detail").GetString());
            Assert.AreEqual("order.locked", response.Json.GetProperty("code").GetString());
            StringAssert.StartsWith(response.Json.GetProperty("type").GetString(), "https://tools.ietf.org/html/rfc9110#");
        }
    }

    [TestMethod]
    public async Task Failure_without_problem_details_services_has_no_trace_id()
    {
        var response = await ExecuteAsync(Result.Failure(s_missing).ToHttpResult());

        Assert.AreEqual("application/problem+json", response.ContentType);
        Assert.IsFalse(response.Json.TryGetProperty("traceId", out _));
    }

    [TestMethod]
    public async Task Failure_goes_through_the_problem_details_service()
    {
        var response = await ExecuteAsync(
            Result.Failure(s_missing).ToHttpResult(),
            services => services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["node"] = "a"));

        Assert.AreEqual("application/problem+json", response.ContentType);
        Assert.IsFalse(string.IsNullOrEmpty(response.Json.GetProperty("traceId").GetString()));
        Assert.AreEqual("a", response.Json.GetProperty("node").GetString());
        Assert.AreEqual("order.not_found", response.Json.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task Failure_for_a_status_without_a_default_takes_the_reason_phrase()
    {
        var response = await ExecuteAsync(Result.Failure(s_missing).ToHttpResult(_ => 429));

        Assert.AreEqual(429, response.Status);
        Assert.AreEqual("Too Many Requests", response.Json.GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task Success_override_builds_the_response()
    {
        var response = await ExecuteAsync(
            Result.Success(new Order(7, "Tea")).ToHttpResult(o => TypedResults.Created($"/orders/{o.Id}", o)));

        Assert.AreEqual(201, response.Status);
        Assert.AreEqual("/orders/7", response.Location);
        Assert.AreEqual(7, response.Json.GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task Success_override_without_a_value_builds_the_response()
    {
        var response = await ExecuteAsync(Result.Success().ToHttpResult(() => TypedResults.Accepted("/jobs/1")));

        Assert.AreEqual(202, response.Status);
        Assert.AreEqual("/jobs/1", response.Location);
    }

    [TestMethod]
    public async Task Success_override_is_not_called_on_failure()
    {
        var called = false;

        var response = await ExecuteAsync(Result.Failure<Order>(s_missing).ToHttpResult(o =>
        {
            called = true;
            return TypedResults.Ok(o);
        }));

        Assert.IsFalse(called);
        Assert.AreEqual(404, response.Status);
    }

    [TestMethod]
    public async Task Status_override_picks_the_status()
    {
        static int Map(Error e) => e.Code == "order.invalid" ? 422 : e.ToStatusCode();

        var invalid = await ExecuteAsync(Result.Failure(Error.Validation("order.invalid", "Bad.")).ToHttpResult(Map));
        var other = await ExecuteAsync(Result.Failure<Order>(Error.Validation("order.empty", "Empty.")).ToHttpResult(statusCode: Map));

        Assert.AreEqual(422, invalid.Status);
        Assert.AreEqual("Unprocessable Entity", invalid.Json.GetProperty("title").GetString());
        Assert.AreEqual(400, other.Status);
    }

    [TestMethod]
    public async Task Both_overrides_combine()
    {
        var created = await ExecuteAsync(Result.Success(1).ToHttpResult(v => TypedResults.Created($"/x/{v}", v), _ => 418));
        var failed = await ExecuteAsync(Result.Failure<int>(s_missing).ToHttpResult(v => TypedResults.Created($"/x/{v}", v), _ => 418));
        var accepted = await ExecuteAsync(Result.Failure(s_missing).ToHttpResult(() => TypedResults.Accepted("/x"), _ => 418));

        Assert.AreEqual(201, created.Status);
        Assert.AreEqual(418, failed.Status);
        Assert.AreEqual(418, accepted.Status);
    }

    [TestMethod]
    public async Task Task_overloads_convert_after_awaiting()
    {
        Assert.AreEqual(204, (await ExecuteAsync(await Task.FromResult(Result.Success()).ToHttpResultAsync())).Status);
        Assert.AreEqual(202, (await ExecuteAsync(await Task.FromResult(Result.Success()).ToHttpResultAsync(() => TypedResults.Accepted("/x")))).Status);
        Assert.AreEqual(200, (await ExecuteAsync(await Task.FromResult(Result.Success(1)).ToHttpResultAsync())).Status);
        Assert.AreEqual(201, (await ExecuteAsync(await Task.FromResult(Result.Success(1)).ToHttpResultAsync(v => TypedResults.Created("/x", v)))).Status);
        Assert.AreEqual(404, (await ExecuteAsync(await Task.FromResult(Result.Failure(s_missing)).ToHttpResultAsync())).Status);
        Assert.AreEqual(418, (await ExecuteAsync(await Task.FromResult(Result.Failure<int>(s_missing)).ToHttpResultAsync(_ => 418))).Status);
    }

    [TestMethod]
    public async Task Null_arguments_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Result)null!).ToHttpResult());
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Result<int>)null!).ToHttpResult());
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Success().ToHttpResult((Func<IResult>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Success(1).ToHttpResult((Func<int, IResult>)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result>)null!).ToHttpResultAsync());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ((Task<Result<int>>)null!).ToHttpResultAsync());
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Task.FromResult(Result.Success()).ToHttpResultAsync((Func<IResult>)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => Task.FromResult(Result.Success(1)).ToHttpResultAsync((Func<int, IResult>)null!));
    }
}
