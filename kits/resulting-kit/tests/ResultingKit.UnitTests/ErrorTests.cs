namespace ResultingKit.UnitTests;

[TestClass]
public sealed class ErrorTests
{
    public static IEnumerable<object[]> Factories =>
    [
        [Error.Failure("c", "m"), ErrorKind.Failure],
        [Error.Validation("c", "m"), ErrorKind.Validation],
        [Error.NotFound("c", "m"), ErrorKind.NotFound],
        [Error.Conflict("c", "m"), ErrorKind.Conflict],
        [Error.Unauthorized("c", "m"), ErrorKind.Unauthorized],
        [Error.Forbidden("c", "m"), ErrorKind.Forbidden],
        [Error.Transient("c", "m"), ErrorKind.Transient],
    ];

    [TestMethod]
    [DynamicData(nameof(Factories))]
    public void Factory_sets_code_message_and_kind(Error error, ErrorKind kind)
    {
        Assert.AreEqual("c", error.Code);
        Assert.AreEqual("m", error.Message);
        Assert.AreEqual(kind, error.Kind);
    }

    [TestMethod]
    public void Kind_defaults_to_failure()
    {
        Assert.AreEqual(ErrorKind.Failure, new Error("c", "m").Kind);
    }

    [TestMethod]
    public void Only_transient_is_retryable()
    {
        foreach (var kind in Enum.GetValues<ErrorKind>())
        {
            Assert.AreEqual(kind == ErrorKind.Transient, new Error("c", "m", kind).IsTransient, kind.ToString());
        }
    }

    [TestMethod]
    public void Errors_compare_by_value()
    {
        Assert.AreEqual(Error.NotFound("order.not_found", "gone"), Error.NotFound("order.not_found", "gone"));
        Assert.AreNotEqual(Error.NotFound("order.not_found", "gone"), Error.Conflict("order.not_found", "gone"));
    }

    [TestMethod]
    public void ToString_is_code_then_message()
    {
        Assert.AreEqual("smtp.rejected: 550 mailbox unavailable", Error.Failure("smtp.rejected", "550 mailbox unavailable").ToString());
    }

    [TestMethod]
    public void Field_errors_default_to_null()
    {
        Assert.IsNull(Error.Validation("c", "m").FieldErrors);
        Assert.IsNull(new Error("c", "m").FieldErrors);
    }

    [TestMethod]
    public void Validation_with_fields_keeps_them()
    {
        var fields = new Dictionary<string, string[]> { ["name"] = ["Is required."] };

        var error = Error.Validation("order.invalid", "name: Is required.", fields);

        Assert.AreEqual(ErrorKind.Validation, error.Kind);
        Assert.AreEqual("order.invalid", error.Code);
        Assert.AreEqual("name: Is required.", error.Message);
        Assert.AreSame(fields, error.FieldErrors);
    }

    [TestMethod]
    public void Empty_field_errors_are_stored_as_null()
    {
        Assert.IsNull(Error.Validation("c", "m", new Dictionary<string, string[]>()).FieldErrors);
        Assert.IsNull((new Error("c", "m") with { FieldErrors = new Dictionary<string, string[]>() }).FieldErrors);
    }

    [TestMethod]
    public void Validation_with_null_fields_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Error.Validation("c", "m", null!));
    }

    [TestMethod]
    public void Any_kind_can_carry_field_errors()
    {
        var error = Error.Conflict("c", "m") with { FieldErrors = new Dictionary<string, string[]> { ["email"] = ["Taken."] } };

        Assert.AreEqual(ErrorKind.Conflict, error.Kind);
        Assert.AreEqual("Taken.", error.FieldErrors!["email"][0]);
    }

    [TestMethod]
    public void Errors_with_the_same_field_errors_are_equal()
    {
        var left = Error.Validation("c", "m", new Dictionary<string, string[]> { ["a"] = ["1", "2"], ["b"] = ["3"] });
        var right = Error.Validation("c", "m", new Dictionary<string, string[]> { ["b"] = ["3"], ["a"] = ["1", "2"] });

        Assert.AreEqual(left, right);
        Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        Assert.IsTrue(left == right);
    }

    [TestMethod]
    public void Errors_with_different_field_errors_are_not_equal()
    {
        var error = Error.Validation("c", "m", new Dictionary<string, string[]> { ["a"] = ["1", "2"] });

        Assert.AreNotEqual(error, Error.Validation("c", "m"));
        Assert.AreNotEqual(Error.Validation("c", "m"), error);
        Assert.AreNotEqual(error, Error.Validation("c", "m", new Dictionary<string, string[]> { ["a"] = ["2", "1"] }));
        Assert.AreNotEqual(error, Error.Validation("c", "m", new Dictionary<string, string[]> { ["b"] = ["1", "2"] }));
        Assert.AreNotEqual(error, Error.Validation("c", "m", new Dictionary<string, string[]> { ["a"] = ["1", "2"], ["b"] = ["3"] }));
        Assert.IsFalse(error.Equals(null));
    }

    [TestMethod]
    public void ToString_ignores_field_errors()
    {
        var error = Error.Validation("order.invalid", "name: Is required.", new Dictionary<string, string[]> { ["name"] = ["Is required."] });

        Assert.AreEqual("order.invalid: name: Is required.", error.ToString());
    }
}
