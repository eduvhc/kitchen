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

    public static int Status(Error error) => error.Code == "order.name_required" ? 422 : error.ToStatusCode();
}

[ApiController]
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC actions must be instance methods.")]
[Route("mvc/orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("{id:int}")]
    public IActionResult Get(int id) => Orders.Find(id).ToActionResult();

    [HttpPost]
    public IActionResult Post(Order order) =>
        Orders.Create(order).ToActionResult(o => CreatedAtAction(nameof(Get), new { id = o.Id }, o), Orders.Status);

    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id) => Orders.CancelAsync(id).ToActionResultAsync();
}

[TestClass]
public sealed class HttpResponseTests
{
    private static WebApplication s_app = null!;
    private static HttpClient s_client = null!;

    [ClassInitialize]
    public static async Task StartAsync(TestContext _)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddControllers().AddApplicationPart(typeof(OrdersController).Assembly);

        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/api/orders/{id:int}", (int id) => Orders.Find(id).ToHttpResult());
        app.MapPost("/api/orders", (Order order) =>
            Orders.Create(order).ToHttpResult(o => TypedResults.Created($"/api/orders/{o.Id}", o), Orders.Status));
        app.MapDelete("/api/orders/{id:int}", (int id) => Orders.CancelAsync(id).ToHttpResultAsync());

        await app.StartAsync();
        s_app = app;
        s_client = app.GetTestClient();
    }

    [ClassCleanup]
    public static async Task StopAsync()
    {
        s_client.Dispose();
        await s_app.DisposeAsync();
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
        return json;
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task Found_is_200_with_the_value(string path)
    {
        var order = await s_client.GetFromJsonAsync<Order>($"{path}/7");

        Assert.AreEqual(new Order(7, "Tea"), order);
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task Missing_is_a_404_problem(string path)
    {
        var json = await AssertProblemAsync(await s_client.GetAsync(new Uri($"{path}/1", UriKind.Relative)), HttpStatusCode.NotFound, "order.not_found");

        Assert.AreEqual("Order 1 does not exist.", json.GetProperty("detail").GetString());
        Assert.AreEqual("https://tools.ietf.org/html/rfc9110#section-15.5.5", json.GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task Minimal_api_problems_go_through_the_problem_details_service()
    {
        var json = await AssertProblemAsync(await s_client.GetAsync(new Uri("/api/orders/1", UriKind.Relative)), HttpStatusCode.NotFound, "order.not_found");

        Assert.IsTrue(json.TryGetProperty("traceId", out _));
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task Forbidden_is_a_403_problem(string path)
    {
        await AssertProblemAsync(await s_client.GetAsync(new Uri($"{path}/13", UriKind.Relative)), HttpStatusCode.Forbidden, "order.forbidden");
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task Created_uses_the_success_override(string path)
    {
        var response = await s_client.PostAsJsonAsync(path, new Order(8, "Coffee"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        StringAssert.EndsWith(response.Headers.Location?.ToString(), "/8", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(new Order(8, "Coffee"), await response.Content.ReadFromJsonAsync<Order>());
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task Invalid_uses_the_status_override(string path)
    {
        var response = await s_client.PostAsJsonAsync(path, new Order(8, " "));

        var json = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "order.name_required");
        Assert.AreEqual("Unprocessable Entity", json.GetProperty("title").GetString());
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task Cancelled_is_204(string path)
    {
        var response = await s_client.DeleteAsync(new Uri($"{path}/7", UriKind.Relative));

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task Conflict_is_a_409_problem(string path)
    {
        await AssertProblemAsync(await s_client.DeleteAsync(new Uri($"{path}/9", UriKind.Relative)), HttpStatusCode.Conflict, "order.shipped");
    }
}
