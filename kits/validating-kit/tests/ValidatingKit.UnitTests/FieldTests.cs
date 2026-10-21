using static ValidatingKit.UnitTests.RuleAssert;

namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class FieldTests
{
    [TestMethod]
    public void Required_fails_only_null()
    {
        Fails<string>(null, f => f.Required(), "Is required.");
        Fails<int?>(null, f => f.Required(), "Is required.");
        Passes("", f => f.Required());
        Passes<int?>(0, f => f.Required());
        Fails<string>(null, f => f.Required("Name it."), "Name it.");
    }

    [TestMethod]
    public void Must_checks_the_value_and_passes_null()
    {
        Passes("abc", f => f.Must(v => v.Length == 3, "Three."));
        Fails("ab", f => f.Must(v => v.Length == 3, "Three."), "Three.");
        Passes<string>(null, f => f.Must(v => v.Length == 3, "Three."));
    }

    [TestMethod]
    public void Equal_and_not_equal_compare_by_default_equality()
    {
        Passes(3, f => f.Equal(3));
        Fails(2, f => f.Equal(3), "Must be equal to 3.");
        Fails("a", f => f.Equal("b"), "Must be equal to 'b'.");
        Passes(2, f => f.NotEqual(3));
        Fails(3, f => f.NotEqual(3), "Must not be equal to 3.");
        Fails(Status.Draft, f => f.NotEqual(Status.Draft), "Must not be equal to Draft.");
        Fails(3, f => f.Equal(4, "Four."), "Four.");
        Fails(3, f => f.NotEqual(3, "Not three."), "Not three.");
        Passes<string>(null, f => f.Equal("b").NotEqual(null!));
    }

    [TestMethod]
    public void First_failure_stops_the_field()
    {
        var calls = 0;

        Fails("", f => f.NotBlank().Must(_ => ++calls > 0, "x").MaxLength(0), "Must not be blank.");

        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Has_failed_reports_a_failed_rule()
    {
        var failed = false;

        Check("", f =>
        {
            var checkedField = f.NotBlank();
            failed = checkedField.HasFailed && !f.HasFailed;
            return checkedField;
        });

        Assert.IsTrue(failed);
    }

    [TestMethod]
    public void Value_exposes_the_field_value()
    {
        string? seen = null;

        Check("tea", f =>
        {
            seen = f.Value;
            return f;
        });

        Assert.AreEqual("tea", seen);
    }

    [TestMethod]
    public void Null_arguments_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Check("a", f => f.Must(null!, "m")));
        Assert.ThrowsExactly<ArgumentNullException>(() => Check("a", f => f.Must(_ => true, null!)));
        Assert.ThrowsExactly<ArgumentNullException>(() => Check("a", f => f.Valid((Func<Validation<string>, Validation<string>>)null!)));
        Assert.ThrowsExactly<ArgumentNullException>(() => Check("a", f => f.Valid((IValidator<string>)null!)));
        Assert.ThrowsExactly<ArgumentNullException>(() => Check<string[]>([], f => f.Each(null!)));
        Assert.ThrowsExactly<ArgumentNullException>(() => Check("a", f => f.Matches((string)null!)));
        Assert.ThrowsExactly<ArgumentNullException>(() => Check("a", f => f.Matches((System.Text.RegularExpressions.Regex)null!)));
    }
}
