using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResultingKit;
using ResultingKit.AspNetCore;

namespace ValidatingKit.AspNetCore.IntegrationTests;

public sealed record Line(string? Sku, int Quantity);

public sealed record PlaceOrder(string? Name, List<Line>? Lines);

public sealed record Note(string? Text);

public sealed class LineValidator : Validator<Line>
{
    public override Validation<Line> Apply(Validation<Line> validation) => validation
        .Field(l => l.Sku, f => f.NotBlank())
        .Field(l => l.Quantity, f => f.GreaterThan(0));
}

public sealed class PlaceOrderValidator(LineValidator lines) : Validator<PlaceOrder>("order.invalid")
{
    public override Validation<PlaceOrder> Apply(Validation<PlaceOrder> validation) => validation
        .Field(o => o.Name, f => f.NotBlank().MaxLength(20))
        .Field(o => o.Lines, f => f.NotEmpty().Each(l => l.Required().Valid(lines)));
}

/// <summary>Counts how many requests resolved it, to prove validators come from the request's services.</summary>
public sealed class ScopedNoteValidator : Validator<Note>
{
    private static int s_created;

    public ScopedNoteValidator() => Interlocked.Increment(ref s_created);

    public static int Created => Volatile.Read(ref s_created);

    public override Validation<Note> Apply(Validation<Note> validation) => validation.Field(n => n.Text, f => f.MaxLength(5));
}

[ApiController]
[Route("mvc")]
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC actions must be instance methods.")]
public sealed class OrdersController : ControllerBase
{
    [HttpPost("orders")]
    public IActionResult Place(PlaceOrder order) => Created($"/mvc/orders/{order.Name}", order);

    [HttpPost("notes/{id:int}")]
    public IActionResult Note(int id, Note note) => Ok(new { id, note.Text });
}

[TestClass]
public sealed class ValidationHttpTests
{
    private static readonly PlaceOrder s_valid = new("Tea", [new Line("SKU-1", 1)]);
    private static readonly PlaceOrder s_invalid = new("", [new Line("SKU-1", 1), new Line("", 0)]);

    private static WebApplication s_app = null!;
    private static HttpClient s_client = null!;

    [ClassInitialize]
    public static async Task StartAsync(TestContext _)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddValidator<LineValidator>().AddValidator<PlaceOrderValidator>();
        builder.Services.AddScoped<IValidator<Note>, ScopedNoteValidator>();
        builder.Services.AddControllers().AddApplicationPart(typeof(OrdersController).Assembly).WithValidation();

        var app = builder.Build();
        app.MapControllers();

        var api = app.MapGroup("/api").WithValidation();
        api.MapPost("/orders", (PlaceOrder order) => TypedResults.Created($"/api/orders/{order.Name}", order));
        api.MapPost("/orders/strict", (PlaceOrder order) => TypedResults.Ok(order)).WithValidation(_ => 422);
        api.MapPost("/notes", (Note note) => TypedResults.Ok(note));
        api.MapGet("/ping", () => "pong");
        api.MapPost("/notes/checked", (Note note, int id) => TypedResults.Ok(new { id, note.Text }))
            .Validate<Note>(n => n.Field(x => x.Text, f => f.NotBlank()), "note.invalid");

        app.MapPost("/open/orders", (PlaceOrder order) => TypedResults.Ok(order));
        app.MapPost("/inline/orders", (PlaceOrder order) => TypedResults.Ok(order))
            .Validate<PlaceOrder>(o => o.Field(x => x.Name, f => f.Equal("Tea")), "order.not_tea", _ => 409);

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
        Assert.AreEqual(code, json.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        return json;
    }

    private static Uri Path(string path) => new(path, UriKind.Relative);

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task A_valid_body_reaches_the_handler(string path)
    {
        var response = await s_client.PostAsJsonAsync(path, s_valid);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(s_valid.Name, (await response.Content.ReadFromJsonAsync<PlaceOrder>())!.Name);
    }

    [TestMethod]
    [DataRow("/api/orders")]
    [DataRow("/mvc/orders")]
    public async Task An_invalid_body_is_a_validation_problem(string path)
    {
        var json = await AssertProblemAsync(await s_client.PostAsJsonAsync(path, s_invalid), HttpStatusCode.BadRequest, "order.invalid");

        Assert.AreEqual("One or more validation errors occurred.", json.GetProperty("title").GetString());
        Assert.AreEqual(
            "name: Must not be blank. lines[1].sku: Must not be blank. lines[1].quantity: Must be greater than 0.",
            json.GetProperty("detail").GetString());

        var errors = json.GetProperty("errors");
        Assert.AreEqual("Must not be blank.", errors.GetProperty("name")[0].GetString());
        Assert.AreEqual("Must not be blank.", errors.GetProperty("lines[1].sku")[0].GetString());
        Assert.AreEqual("Must be greater than 0.", errors.GetProperty("lines[1].quantity")[0].GetString());
    }

    [TestMethod]
    public async Task An_endpoint_override_in_a_validated_group_picks_the_status()
    {
        await AssertProblemAsync(await s_client.PostAsJsonAsync("/api/orders/strict", s_invalid), HttpStatusCode.UnprocessableEntity, "order.invalid");
        Assert.AreEqual(HttpStatusCode.OK, (await s_client.PostAsJsonAsync("/api/orders/strict", s_valid)).StatusCode);
    }

    [TestMethod]
    public async Task Endpoints_outside_a_validated_group_are_not_validated()
    {
        Assert.AreEqual(HttpStatusCode.OK, (await s_client.PostAsJsonAsync("/open/orders", s_invalid)).StatusCode);
    }

    [TestMethod]
    public async Task Endpoints_with_nothing_to_validate_get_no_filter()
    {
        Assert.AreEqual("pong", await s_client.GetStringAsync(Path("/api/ping")));

        var ping = Endpoint("/api/ping");
        Assert.IsFalse(ping.Metadata.OfType<IProducesResponseTypeMetadata>().Any(m => m.StatusCode == 400));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task Validators_are_resolved_from_the_request_services()
    {
        var before = ScopedNoteValidator.Created;

        Assert.AreEqual(HttpStatusCode.OK, (await s_client.PostAsJsonAsync("/api/notes", new Note("short"))).StatusCode);
        await AssertProblemAsync(await s_client.PostAsJsonAsync("/api/notes", new Note("too long")), HttpStatusCode.BadRequest, Validation.DefaultCode);

        Assert.AreEqual(before + 2, ScopedNoteValidator.Created);
    }

    [TestMethod]
    public async Task Inline_rules_validate_their_argument_alongside_registered_validators()
    {
        var blank = await AssertProblemAsync(
            await s_client.PostAsJsonAsync("/api/notes/checked?id=1", new Note("")), HttpStatusCode.BadRequest, "note.invalid");
        Assert.AreEqual("text: Must not be blank.", blank.GetProperty("detail").GetString());

        await AssertProblemAsync(
            await s_client.PostAsJsonAsync("/api/notes/checked?id=1", new Note("too long")), HttpStatusCode.BadRequest, Validation.DefaultCode);

        Assert.AreEqual(HttpStatusCode.OK, (await s_client.PostAsJsonAsync("/api/notes/checked?id=1", new Note("ok"))).StatusCode);
    }

    [TestMethod]
    public async Task Inline_rules_take_their_own_code_and_status()
    {
        var json = await AssertProblemAsync(
            await s_client.PostAsJsonAsync("/inline/orders", new PlaceOrder("Coffee", null)), HttpStatusCode.Conflict, "order.not_tea");

        Assert.AreEqual("name: Must be equal to 'Tea'.", json.GetProperty("detail").GetString());
        Assert.AreEqual(HttpStatusCode.OK, (await s_client.PostAsJsonAsync("/inline/orders", new PlaceOrder("Tea", null))).StatusCode);
    }

    [TestMethod]
    public async Task Controllers_validate_every_argument_with_a_validator()
    {
        await AssertProblemAsync(await s_client.PostAsJsonAsync("/mvc/notes/7", new Note("too long")), HttpStatusCode.BadRequest, Validation.DefaultCode);
        Assert.AreEqual(HttpStatusCode.OK, (await s_client.PostAsJsonAsync("/mvc/notes/7", new Note("ok"))).StatusCode);
    }

    [TestMethod]
    [DataRow("/api/orders", true)]
    [DataRow("/api/notes/checked", true)]
    [DataRow("/api/orders/strict", false)]
    [DataRow("/open/orders", false)]
    [DataRow("/inline/orders", false)]
    public void Validated_endpoints_document_the_validation_problem(string route, bool documented)
    {
        var produces = Endpoint(route).Metadata.OfType<IProducesResponseTypeMetadata>()
            .Any(m => m.StatusCode == 400 && m.Type == typeof(HttpValidationProblemDetails) && m.ContentTypes.Contains("application/problem+json"));

        Assert.AreEqual(documented, produces);
    }

    [TestMethod]
    public void Inline_rules_for_a_type_the_handler_does_not_take_fail_at_startup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        using var app = builder.Build();
        app.MapPost("/x", (PlaceOrder order) => order).Validate<Note>(n => n);

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            _ = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToList());
        StringAssert.Contains(error.Message, "Validate<Note>()");
        StringAssert.Contains(error.Message, "no Note parameter");
    }

    private static RouteEndpoint Endpoint(string route) =>
        s_app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => "/" + e.RoutePattern.RawText!.TrimStart('/') == route);
}
