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
}
