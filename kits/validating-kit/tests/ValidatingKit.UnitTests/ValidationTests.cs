namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class ValidationTests
{
    private static Result Validate(DeleteFileRequested? request) =>
        Validation.That(request, "INVALID_REQUEST")
            .Required()
            .Field(r => r.StorageId, f => f.NotBlank())
            .ToResult();

    private static Error AssertInvalid(Result result)
    {
        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorKind.Validation, result.Error.Kind);
        Assert.IsNotNull(result.Error.FieldErrors);
        return result.Error;
    }

    [TestMethod]
    public void Valid_value_is_the_shared_success()
    {
        Assert.AreSame(Result.Success(), Validate(new DeleteFileRequested("s-1")));
    }

    [TestMethod]
    public void Null_value_fails_as_required_under_the_argument_name()
    {
        var error = AssertInvalid(Validate(null));

        Assert.AreEqual("INVALID_REQUEST", error.Code);
        Assert.AreEqual("request: Is required.", error.Message);
        CollectionAssert.AreEqual(new[] { "Is required." }, error.FieldErrors![""]);
    }

    [TestMethod]
    public void Invalid_field_is_reported_by_its_camel_case_path()
    {
        var error = AssertInvalid(Validate(new DeleteFileRequested(" ")));

        Assert.AreEqual("storageId: Must not be blank.", error.Message);
        Assert.AreEqual(1, error.FieldErrors!.Count);
        CollectionAssert.AreEqual(new[] { "Must not be blank." }, error.FieldErrors["storageId"]);
    }

    [TestMethod]
    public void Null_value_fails_even_without_required()
    {
        DeleteFileRequested? request = null;

        var error = AssertInvalid(Validation.That(request, "c").Field(r => r.StorageId, f => f.NotBlank()).ToResult());

        Assert.AreEqual("request: Is required.", error.Message);
    }

    [TestMethod]
    public void Required_message_can_be_replaced()
    {
        var error = AssertInvalid(Validation.That((DeleteFileRequested?)null, "c").Required("Send a request.").ToResult());

        Assert.AreEqual("value: Send a request.", error.Message);
        Assert.AreEqual(1, error.FieldErrors!.Count);
    }

    [TestMethod]
    public void Root_name_can_be_passed()
    {
        var error = AssertInvalid(Validation.That<DeleteFileRequested>(null, "c", "body").ToResult());

        Assert.AreEqual("body: Is required.", error.Message);
    }

    [TestMethod]
    public void Empty_code_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Validation.That(1, ""));
        Assert.ThrowsExactly<ArgumentNullException>(() => Validation.That(1, null!));
    }

    [TestMethod]
    public void Every_field_is_reported_but_each_only_once()
    {
        var order = new Order { Name = "", Email = "nope" };

        var error = AssertInvalid(Validation.That(order, "order.invalid")
            .Field(o => o.Name, f => f.NotBlank().MaxLength(3))
            .Field(o => o.Email, f => f.NotBlank().Email().MaxLength(2))
            .Field(o => o.CustomerId, f => f.NotEmpty())
            .ToResult());

        Assert.AreEqual(3, error.FieldErrors!.Count);
        CollectionAssert.AreEqual(new[] { "Must not be blank." }, error.FieldErrors["name"]);
        CollectionAssert.AreEqual(new[] { "Must be a valid email address." }, error.FieldErrors["email"]);
        CollectionAssert.AreEqual(new[] { "Must not be empty." }, error.FieldErrors["customerId"]);
    }

    [TestMethod]
    public void Message_lists_failures_in_the_order_they_were_found()
    {
        var order = new Order { Name = "", Email = "nope" };

        var error = AssertInvalid(Validation.That(order, "order.invalid")
            .Field(o => o.Email, f => f.Email())
            .Must(o => o.Ship, "Must be shipped.")
            .Field(o => o.Name, f => f.NotBlank())
            .ToResult());

        Assert.AreEqual(
            "email: Must be a valid email address. order: Must be shipped. name: Must not be blank.",
            error.Message);
        CollectionAssert.AreEqual(new[] { "email", "", "name" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public void Repeated_fields_collect_their_messages_under_one_path()
    {
        var error = AssertInvalid(Validation.That(new Order { Name = "x" }, "c")
            .Field(o => o.Name, f => f.MinLength(2))
            .Field(o => o.Name, f => f.Matches("^[A-Z]"))
            .ToResult());

        CollectionAssert.AreEqual(
            new[] { "Must be at least 2 characters long.", "Is not in the correct format." },
            error.FieldErrors!["name"]);
        Assert.AreEqual("name: Must be at least 2 characters long. name: Is not in the correct format.", error.Message);
    }

    [TestMethod]
    public void Rules_can_be_chained_one_statement_at_a_time()
    {
        var validation = Validation.That(new Order(), "c");
        validation.Field(o => o.Name, f => f.NotBlank());
        validation.Field(o => o.Email, f => f.NotBlank());

        Assert.AreEqual(2, AssertInvalid(validation.ToResult()).FieldErrors!.Count);
    }

    [TestMethod]
    public void Explicit_names_replace_the_derived_one()
    {
        var error = AssertInvalid(Validation.That(new Order { Lines = [] }, "c")
            .Field("lineCount", o => o.Lines!.Count, f => f.GreaterThan(0))
            .Field("window", o => o.End, (f, o) => f.GreaterThan(o.Start, "Must end after it starts."))
            .ToResult());

        CollectionAssert.AreEqual(new[] { "Must be greater than 0." }, error.FieldErrors!["lineCount"]);
        CollectionAssert.AreEqual(new[] { "Must end after it starts." }, error.FieldErrors["window"]);
    }

    [TestMethod]
    public void Cross_field_rules_read_the_whole_value()
    {
        var order = new Order { Start = new DateOnly(2026, 5, 2), End = new DateOnly(2026, 5, 1) };

        var error = AssertInvalid(Validation.That(order, "c")
            .Field(o => o.End, (f, o) => f.GreaterThanOrEqualTo(o.Start))
            .ToResult());

        CollectionAssert.AreEqual(new[] { "Must be greater than or equal to 2026-05-02." }, error.FieldErrors!["end"]);
    }

    [TestMethod]
    public void Must_on_the_value_reports_at_the_root()
    {
        var error = AssertInvalid(Validation.That(new Order(), "c", "").Must(o => o.Ship, "Must ship.").ToResult());

        CollectionAssert.AreEqual(new[] { "Must ship." }, error.FieldErrors![""]);
        Assert.AreEqual("value: Must ship.", error.Message);
    }

    [TestMethod]
    public void When_applies_rules_only_if_the_condition_holds()
    {
        static Result Check(Order order) => Validation.That(order, "c")
            .When(o => o.Ship, v => v.Field(o => o.Address, f => f.Required()))
            .ToResult();

        Assert.IsTrue(Check(new Order { Ship = false }).IsSuccess);
        CollectionAssert.AreEqual(new[] { "Is required." }, AssertInvalid(Check(new Order { Ship = true })).FieldErrors!["address"]);
    }

    [TestMethod]
    public void Field_rules_can_branch_on_the_whole_value()
    {
        static Result Check(Order order) => Validation.That(order, "c")
            .Field(o => o.Email, (f, o) => o.Ship ? f.NotBlank() : f)
            .ToResult();

        Assert.IsTrue(Check(new Order()).IsSuccess);
        Assert.IsTrue(Check(new Order { Ship = true }).IsFailure);
    }

    [TestMethod]
    public void Include_applies_a_validator_to_the_value()
    {
        var error = AssertInvalid(Validation.That(new Address("", "123456789"), "c").Include(new AddressValidator()).ToResult());

        Assert.AreEqual("c", error.Code);
        Assert.AreEqual("street: Must not be blank. postalCode: Must be at most 8 characters long.", error.Message);
    }

    [TestMethod]
    public void A_validated_primitive_can_be_its_own_field()
    {
        var error = AssertInvalid(Validation.That("nope", "email.invalid").Field(e => e, f => f.Email()).ToResult());

        CollectionAssert.AreEqual(new[] { "Must be a valid email address." }, error.FieldErrors![""]);
        Assert.AreEqual("value: Must be a valid email address.", error.Message);
    }

    [TestMethod]
    public void Value_result_carries_the_value_and_chains()
    {
        var order = new Order { Name = "Tea" };

        var shipped = Validation.That(order, "c")
            .Field(o => o.Name, f => f.NotBlank())
            .ToValueResult()
            .Bind(o => Result.Success(o.Name!.Length));

        Assert.AreEqual(3, shipped.Value);
    }

    [TestMethod]
    public void Value_result_failure_skips_the_chain()
    {
        var called = false;

        var result = Validation.That(new Order(), "c")
            .Field(o => o.Name, f => f.NotBlank())
            .ToValueResult()
            .Bind(o =>
            {
                called = true;
                return Result.Success(o);
            });

        Assert.IsFalse(called);
        Assert.AreEqual("name: Must not be blank.", result.Error!.Message);
    }

    [TestMethod]
    public void Value_result_of_null_fails()
    {
        Assert.AreEqual("order: Is required.", Validation.That((Order?)null, "c", "order").ToValueResult().Error!.Message);
    }

    [TestMethod]
    public void Rules_and_selectors_are_skipped_on_a_null_value()
    {
        var called = false;

        var result = Validation.That((Order?)null, "c")
            .Field(o =>
            {
                called = true;
                return o.Name;
            }, f => f.NotBlank(), "o => o.Name")
            .Must(_ => called = true, "x")
            .When(_ => called = true, v => v)
            .ToResult();

        Assert.IsFalse(called);
        Assert.AreEqual(1, AssertInvalid(result).FieldErrors!.Count);
    }

    [TestMethod]
    public void Unnameable_selectors_are_rejected()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() =>
            Validation.That(new Order(), "c").Field(o => o.Name!.Trim(), f => f.NotBlank()));

        StringAssert.Contains(error.Message, "o => o.Name!.Trim()");
    }

    [TestMethod]
    public void Null_arguments_are_rejected()
    {
        var validation = Validation.That(new Order(), "c");

        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Field<string>(null!, f => f));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Field(o => o.Name, (Func<Field<string>, Field<string>>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Field(o => o.Name, (Func<Field<string>, Order, Field<string>>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Field(null!, o => o.Name, f => f));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Field(null!, o => o.Name, (f, _) => f));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Must(null!, "m"));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Must(_ => true, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.When(null!, v => v));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.When(_ => true, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => validation.Include(null!));
    }

    [TestMethod]
    public void A_validation_converts_to_a_result_where_one_is_expected()
    {
        static Result Check(DeleteFileRequested? request) =>
            Validation.That(request, "INVALID_REQUEST").Field(r => r.StorageId, f => f.NotBlank());

        Assert.AreSame(Result.Success(), Check(new DeleteFileRequested("s-1")));
        Assert.AreEqual("storageId: Must not be blank.", Check(new DeleteFileRequested("")).Error!.Message);
    }

    [TestMethod]
    public void A_validation_converts_to_a_value_result_where_one_is_expected()
    {
        static Result<Order> Check(Order? order) => Validation.That(order).Field(o => o.Name, f => f.NotBlank());

        var order = new Order { Name = "Tea" };

        Assert.AreSame(order, Check(order).Value);
        Assert.IsTrue(Check(new Order()).IsFailure);
    }

    [TestMethod]
    public async Task A_validation_converts_inside_an_async_method()
    {
        static async Task<Result> CheckAsync(Order order)
        {
            await Task.Yield();
            return Validation.That(order).Field(o => o.Name, f => f.NotBlank());
        }

        Assert.IsTrue((await CheckAsync(new Order())).IsFailure);
    }

    [TestMethod]
    public void Code_defaults_to_validation_failed()
    {
        Assert.AreEqual("validation.failed", Validation.That((Order?)null).ToResult().Error!.Code);
    }
}
