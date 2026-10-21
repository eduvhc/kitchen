namespace ValidatingKit.UnitTests;

/// <summary>The README's examples, kept compiling and true.</summary>
[TestClass]
public sealed class ReadmeTests
{
    private sealed record OrderLine(string? Sku, int Quantity, bool Gift);

    private sealed record PlaceOrder(Guid CustomerId, List<OrderLine>? Lines, string? Phone, bool ContactByPhone);

    private sealed class OrderLineValidator() : Validator<OrderLine>("line.invalid")
    {
        public override Validation<OrderLine> Apply(Validation<OrderLine> line) => line
            .Field(l => l.Sku, f => f.NotBlank().Must(sku => sku.StartsWith("SKU-", StringComparison.Ordinal), "Must start with SKU-."))
            .Field(l => l.Quantity, f => f.GreaterThan(0));
    }

    private static readonly OrderLineValidator s_lines = new();

    private static Task<Result<PlaceOrder>> HandleAsync(PlaceOrder? command) =>
        Validation.That(command, "order.invalid")
            .Field(c => c.CustomerId, f => f.NotEmpty())
            .Field(c => c.Lines, f => f.NotEmpty().Each(l => l.Required().Valid(s_lines)))
            .Field(c => c.Phone, (f, c) => c.ContactByPhone ? f.NotBlank() : f)
            .Field("giftCount", c => c.Lines?.Count(l => l.Gift), f => f.LessThanOrEqualTo(3))
            .ToValueResult()
            .BindAsync(c => Task.FromResult(Result.Success(c)));

    [TestMethod]
    public async Task The_handler_example_reports_every_field()
    {
        var command = new PlaceOrder(Guid.Empty, [new OrderLine("SKU-1", 1, true), new OrderLine("X", 0, true)], null, true);

        var error = (await HandleAsync(command)).Error!;

        Assert.AreEqual(
            "customerId: Must not be empty. lines[1].sku: Must start with SKU-. lines[1].quantity: Must be greater than 0. phone: Must not be blank.",
            error.Message);
        CollectionAssert.AreEqual(new[] { "customerId", "lines[1].sku", "lines[1].quantity", "phone" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public async Task The_handler_example_passes_a_valid_command_on()
    {
        var command = new PlaceOrder(Guid.NewGuid(), [new OrderLine("SKU-1", 1, false)], null, false);

        Assert.AreSame(command, (await HandleAsync(command)).Value);
    }

    [TestMethod]
    public async Task The_handler_example_names_explicit_fields()
    {
        var gifts = Enumerable.Range(0, 4).Select(i => new OrderLine($"SKU-{i}", 1, true)).ToList();

        var error = (await HandleAsync(new PlaceOrder(Guid.NewGuid(), gifts, null, false))).Error!;

        Assert.AreEqual("giftCount: Must be less than or equal to 3.", error.Message);
    }
}
