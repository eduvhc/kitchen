namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class NestedTests
{
    private static readonly AddressValidator s_address = new();

    [TestMethod]
    public void Nested_failures_are_prefixed_with_the_field_path()
    {
        var order = new Order { Address = new Address(" ", "123456789") };

        var error = Validation.That(order, "order.invalid")
            .Field(o => o.Address, f => f.Required().Valid(s_address))
            .ToResult().Error!;

        Assert.AreEqual("order.invalid", error.Code);
        CollectionAssert.AreEqual(new[] { "address.street", "address.postalCode" }, error.FieldErrors!.Keys.ToArray());
        Assert.AreEqual("address.street: Must not be blank. address.postalCode: Must be at most 8 characters long.", error.Message);
    }

    [TestMethod]
    public void Optional_child_is_skipped_when_null()
    {
        Assert.IsTrue(Validation.That(new Order(), "c").Field(o => o.Address, f => f.Valid(s_address)).ToResult().IsSuccess);
    }

    [TestMethod]
    public void Required_child_fails_at_its_own_path()
    {
        var error = Validation.That(new Order(), "c").Field(o => o.Address, f => f.Required().Valid(s_address)).ToResult().Error!;

        Assert.AreEqual("address: Is required.", error.Message);
    }

    [TestMethod]
    public void Inline_nested_rules_are_prefixed()
    {
        var order = new Order { Address = new Address("Main St", null) };

        var error = Validation.That(order, "c")
            .Field(o => o.Address, f => f.Valid(a => a.Field(x => x.PostalCode, p => p.Required())))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "address.postalCode" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public void Nested_root_rules_report_at_the_field()
    {
        var order = new Order { Address = new Address("", "") };

        var error = Validation.That(order, "c")
            .Field(o => o.Address, f => f.Valid(a => a.Must(x => x.Street == x.PostalCode, "Must differ.").Must(_ => false, "Never.")))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "Never." }, error.FieldErrors!["address"]);
    }

    [TestMethod]
    public void A_failed_child_stops_the_field()
    {
        var order = new Order { Address = new Address("", "1") };

        var error = Validation.That(order, "c")
            .Field(o => o.Address, f => f.Valid(s_address).Must(_ => false, "Unreached."))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "address.street" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public void A_valid_child_lets_the_field_continue()
    {
        var order = new Order { Address = new Address("Main", "1") };

        var error = Validation.That(order, "c")
            .Field(o => o.Address, f => f.Valid(s_address).Must(_ => false, "Reached."))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "Reached." }, error.FieldErrors!["address"]);
    }

    [TestMethod]
    public void Deep_paths_combine_every_level()
    {
        var shipment = new Tuple<Order>(new Order { Lines = [new Line("A", 1), new Line(null, 1)] });

        var error = Validation.That(shipment, "c")
            .Field("order", s => s.Item1, f => f.Valid(o => o.Field(x => x.Lines, l => l.Each(line => line.Valid(new LineValidator())))))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "order.lines[1].sku" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public void Member_chains_name_every_segment()
    {
        var order = new Order { Address = new Address("", null) };

        var error = Validation.That(order, "c").Field(o => o.Address!.Street, f => f.NotBlank()).ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "address.street" }, error.FieldErrors!.Keys.ToArray());
    }
}
