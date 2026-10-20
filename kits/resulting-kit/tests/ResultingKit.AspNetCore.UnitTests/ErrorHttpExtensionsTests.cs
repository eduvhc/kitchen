namespace ResultingKit.AspNetCore.UnitTests;

[TestClass]
public sealed class ErrorHttpExtensionsTests
{
    [TestMethod]
    [DataRow(ErrorKind.Validation, 400)]
    [DataRow(ErrorKind.Unauthorized, 401)]
    [DataRow(ErrorKind.Forbidden, 403)]
    [DataRow(ErrorKind.NotFound, 404)]
    [DataRow(ErrorKind.Conflict, 409)]
    [DataRow(ErrorKind.Transient, 503)]
    [DataRow(ErrorKind.Failure, 500)]
    public void Every_kind_has_a_status(ErrorKind kind, int status)
    {
        Assert.AreEqual(status, kind.ToStatusCode());
        Assert.AreEqual(status, new Error("x", "y", kind).ToStatusCode());
    }

    [TestMethod]
    public void Every_kind_is_mapped_explicitly()
    {
        // Guards against a new ErrorKind silently landing on the 500 fallback.
        var failures = Enum.GetValues<ErrorKind>().Count(k => k.ToStatusCode() == 500);

        Assert.AreEqual(1, failures);
    }

    [TestMethod]
    public void Unknown_kind_is_a_500()
    {
        Assert.AreEqual(500, ((ErrorKind)999).ToStatusCode());
    }

    [TestMethod]
    public void Null_error_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Error)null!).ToStatusCode());
    }
}
