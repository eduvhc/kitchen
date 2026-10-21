using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ResultingKit.AspNetCore.IntegrationTests;

public sealed record Order(int Id, string Name);

public static class Orders
{
    public static Result<Order> Find(int id) => id switch
    {
        7 => new Order(7, "Tea"),
        13 => Error.Forbidden("order.forbidden", "Order 13 belongs to someone else."),
        _ => Error.NotFound("order.not_found", $"Order {id} does not exist."),
    };

    public static Result<Order> Create(Order order) => string.IsNullOrWhiteSpace(order.Name)
        ? Error.Validation("order.name_required", "An order needs a name.")
        : order;

    public static Task<Result> CancelAsync(int id) => Task.FromResult(id == 7
        ? Result.Success()
        : Error.Conflict("order.shipped", $"Order {id} has shipped."));

    public static Result Rename(string name) => Error.Validation(
        "order.invalid",
        "name: Must be at most 3 characters long.",
        new Dictionary<string, string[]> { ["name"] = ["Must be at most 3 characters long."] });

    public static Result Fail(ErrorKind kind) => new Error($"kind.{kind}", $"Failed as {kind}.", kind);

    public static int Status(Error error) => error.Code == "order.name_required" ? 422 : error.ToStatusCode();
}

[ApiController]
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC actions must be instance methods.")]
[Route("mvc")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("orders/{id:int}")]
    public IActionResult Get(int id) => Orders.Find(id).ToActionResult();

    [HttpPost("orders")]
    public IActionResult Post(Order order) =>
        Orders.Create(order).ToActionResult(o => CreatedAtAction(nameof(Get), new { id = o.Id }, o), Orders.Status);

    [HttpDelete("orders/{id:int}")]
    public Task<IActionResult> Delete(int id) => Orders.CancelAsync(id).ToActionResultAsync();

    [HttpPut("orders/7/name")]
    public IActionResult Rename([FromQuery] string name) => Orders.Rename(name).ToActionResult();

    [HttpGet("kinds/{kind}")]
    public IActionResult Kind(ErrorKind kind) => Orders.Fail(kind).ToActionResult();
}

[TestClass]
public sealed class HttpResponseTests
{
    private static WebApplication s_app = null!;
    private static HttpClient s_client = null!;
    private static WebApplication s_bareApp = null!;
    private static HttpClient s_bareClient = null!;

    [ClassInitialize]
    public static async Task StartAsync(TestContext _)
    {
        s_app = await StartAsync(services =>
            services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["node"] = "kitchen-1"));
        s_client = s_app.GetTestClient();

        s_bareApp = await StartAsync(_ => { });
        s_bareClient = s_bareApp.GetTestClient();
    }

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        configure(builder.Services);
        builder.Services.AddControllers().AddApplicationPart(typeof(OrdersController).Assembly);

        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/api/orders/{id:int}", (int id) => Orders.Find(id).ToHttpResult());
        app.MapPost("/api/orders", (Order order) =>
            Orders.Create(order).ToHttpResult(o => TypedResults.Created($"/api/orders/{o.Id}", o), Orders.Status));
        app.MapDelete("/api/orders/{id:int}", (int id) => Orders.CancelAsync(id).ToHttpResultAsync());
        app.MapPut("/api/orders/7/name", (string name) => Orders.Rename(name).ToHttpResult());
        app.MapGet("/api/kinds/{kind}", (ErrorKind kind) => Orders.Fail(kind).ToHttpResult());

        await app.StartAsync();
        return app;
    }

    [ClassCleanup]
    public static async Task StopAsync()
    {
        s_client.Dispose();
        s_bareClient.Dispose();
        await s_app.DisposeAsync();
        await s_bareApp.DisposeAsync();
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual((int)status, json.GetProperty("status").GetInt32());
        Assert.AreEqual(code, json.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("title").GetString()));
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("detail").GetString()));
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        Assert.AreEqual("kitchen-1", json.GetProperty("node").GetString());
        return json;
    }

    private static Uri Path(string path) => new(path, UriKind.Relative);

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Found_is_200_with_the_value(string root)
    {
        var order = await s_client.GetFromJsonAsync<Order>($"{root}/orders/7");

        Assert.AreEqual(new Order(7, "Tea"), order);
    }

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Missing_is_a_404_problem(string root)
    {
        var json = await AssertProblemAsync(await s_client.GetAsync(Path($"{root}/orders/1")), HttpStatusCode.NotFound, "order.not_found");

        Assert.AreEqual("Not Found", json.GetProperty("title").GetString());
        Assert.AreEqual("Order 1 does not exist.", json.GetProperty("detail").GetString());
        Assert.AreEqual("https://tools.ietf.org/html/rfc9110#section-15.5.5", json.GetProperty("type").GetString());
    }

    [TestMethod]
    [DataRow("/api", ErrorKind.Validation, HttpStatusCode.BadRequest)]
    [DataRow("/api", ErrorKind.Unauthorized, HttpStatusCode.Unauthorized)]
    [DataRow("/api", ErrorKind.Forbidden, HttpStatusCode.Forbidden)]
    [DataRow("/api", ErrorKind.NotFound, HttpStatusCode.NotFound)]
    [DataRow("/api", ErrorKind.Conflict, HttpStatusCode.Conflict)]
    [DataRow("/api", ErrorKind.Transient, HttpStatusCode.ServiceUnavailable)]
    [DataRow("/api", ErrorKind.Failure, HttpStatusCode.InternalServerError)]
    [DataRow("/mvc", ErrorKind.Validation, HttpStatusCode.BadRequest)]
    [DataRow("/mvc", ErrorKind.Unauthorized, HttpStatusCode.Unauthorized)]
    [DataRow("/mvc", ErrorKind.Forbidden, HttpStatusCode.Forbidden)]
    [DataRow("/mvc", ErrorKind.NotFound, HttpStatusCode.NotFound)]
    [DataRow("/mvc", ErrorKind.Conflict, HttpStatusCode.Conflict)]
    [DataRow("/mvc", ErrorKind.Transient, HttpStatusCode.ServiceUnavailable)]
    [DataRow("/mvc", ErrorKind.Failure, HttpStatusCode.InternalServerError)]
    public async Task Every_kind_is_a_problem_with_its_status(string root, ErrorKind kind, HttpStatusCode status)
    {
        var json = await AssertProblemAsync(await s_client.GetAsync(Path($"{root}/kinds/{kind}")), status, $"kind.{kind}");

        Assert.AreEqual($"Failed as {kind}.", json.GetProperty("detail").GetString());
        StringAssert.StartsWith(json.GetProperty("type").GetString(), "https://tools.ietf.org/html/rfc9110#");
    }

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Forbidden_is_a_403_problem(string root)
    {
        await AssertProblemAsync(await s_client.GetAsync(Path($"{root}/orders/13")), HttpStatusCode.Forbidden, "order.forbidden");
    }

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Created_uses_the_success_override(string root)
    {
        var response = await s_client.PostAsJsonAsync($"{root}/orders", new Order(8, "Coffee"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        StringAssert.EndsWith(response.Headers.Location?.ToString(), "/8", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(new Order(8, "Coffee"), await response.Content.ReadFromJsonAsync<Order>());
    }

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Invalid_uses_the_status_override(string root)
    {
        var response = await s_client.PostAsJsonAsync($"{root}/orders", new Order(8, " "));

        var json = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "order.name_required");
        Assert.AreEqual("Unprocessable Entity", json.GetProperty("title").GetString());
    }

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Field_errors_are_a_validation_problem(string root)
    {
        var response = await s_client.PutAsync(Path($"{root}/orders/7/name?name=Coffee"), null);

        var json = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "order.invalid");
        Assert.AreEqual("One or more validation errors occurred.", json.GetProperty("title").GetString());
        Assert.AreEqual("name: Must be at most 3 characters long.", json.GetProperty("detail").GetString());
        Assert.AreEqual("Must be at most 3 characters long.", json.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Cancelled_is_204(string root)
    {
        var response = await s_client.DeleteAsync(Path($"{root}/orders/7"));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api")]
    [DataRow("/mvc")]
    public async Task Conflict_is_a_409_problem(string root)
    {
        await AssertProblemAsync(await s_client.DeleteAsync(Path($"{root}/orders/9")), HttpStatusCode.Conflict, "order.shipped");
    }

    [TestMethod]
    public async Task Without_add_problem_details_controllers_still_have_a_trace_id()
    {
        var response = await s_bareClient.GetAsync(Path("/mvc/orders/1"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("order.not_found", json.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
    }

    [TestMethod]
    public async Task Without_add_problem_details_minimal_apis_have_no_trace_id()
    {
        var response = await s_bareClient.GetAsync(Path("/api/orders/1"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("order.not_found", json.GetProperty("code").GetString());
        Assert.AreEqual("Not Found", json.GetProperty("title").GetString());
        Assert.IsFalse(json.TryGetProperty("traceId", out _));
    }
}
