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
    [DataRow(ErrorKind.Validation, 400, "Bad Request", "https://tools.ietf.org/html/rfc9110#section-15.5.1")]
    [DataRow(ErrorKind.Unauthorized, 401, "Unauthorized", "https://tools.ietf.org/html/rfc9110#section-15.5.2")]
    [DataRow(ErrorKind.Forbidden, 403, "Forbidden", "https://tools.ietf.org/html/rfc9110#section-15.5.4")]
    [DataRow(ErrorKind.NotFound, 404, "Not Found", "https://tools.ietf.org/html/rfc9110#section-15.5.5")]
    [DataRow(ErrorKind.Conflict, 409, "Conflict", "https://tools.ietf.org/html/rfc9110#section-15.5.10")]
    [DataRow(ErrorKind.Transient, 503, "Service Unavailable", "https://tools.ietf.org/html/rfc9110#section-15.6.4")]
    [DataRow(ErrorKind.Failure, 500, "Internal Server Error", "https://tools.ietf.org/html/rfc9110#section-15.6.1")]
    public void Problem_describes_the_error(ErrorKind kind, int status, string title, string type)
    {
        var problem = new Error("order.locked", "Order 42 is locked.", kind).ToProblemDetails();

        Assert.AreEqual(status, problem.Status);
        Assert.AreEqual(title, problem.Title);
        Assert.AreEqual(type, problem.Type);
        Assert.AreEqual("Order 42 is locked.", problem.Detail);
        Assert.AreEqual("order.locked", problem.Extensions[ErrorHttpExtensions.CodeExtension]);
    }

    [TestMethod]
    public void Problem_uses_the_status_override()
    {
        var problem = Error.Validation("order.invalid", "Bad.").ToProblemDetails(_ => 422);

        Assert.AreEqual(422, problem.Status);
        Assert.AreEqual("Unprocessable Entity", problem.Title);
        Assert.AreEqual("https://tools.ietf.org/html/rfc9110#section-15.5.21", problem.Type);
    }

    [TestMethod]
    public void Problem_for_a_status_without_a_known_type_has_no_type()
    {
        var problem = Error.Failure("x", "y").ToProblemDetails(_ => 429);

        Assert.AreEqual("Too Many Requests", problem.Title);
        Assert.IsNull(problem.Type);
    }

    [TestMethod]
    public void Problem_for_a_status_without_a_reason_phrase_has_no_title()
    {
        Assert.IsNull(Error.Failure("x", "y").ToProblemDetails(_ => 599).Title);
    }

    [TestMethod]
    public void Null_error_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Error)null!).ToStatusCode());
        Assert.ThrowsExactly<ArgumentNullException>(() => ((Error)null!).ToProblemDetails());
    }
}
