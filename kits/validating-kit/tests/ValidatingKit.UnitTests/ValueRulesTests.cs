using static ValidatingKit.UnitTests.RuleAssert;

namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class ValueRulesTests
{
    [TestMethod]
    public void Guid_not_empty()
    {
        Passes(Guid.NewGuid(), f => f.NotEmpty());
        Fails(Guid.Empty, f => f.NotEmpty(), "Must not be empty.");
        Fails(Guid.Empty, f => f.NotEmpty("Pick one."), "Pick one.");
    }

    [TestMethod]
    public void Nullable_guid_not_empty_fails_null()
    {
        Passes<Guid?>(Guid.NewGuid(), f => f.NotEmpty());
        Fails<Guid?>(Guid.Empty, f => f.NotEmpty(), "Must not be empty.");
        Fails<Guid?>(null, f => f.NotEmpty(), "Must not be empty.");
        Fails<Guid?>(null, f => f.NotEmpty("Pick one."), "Pick one.");
    }

    [TestMethod]
    public void Is_in_enum()
    {
        Passes(Status.Placed, f => f.IsInEnum());
        Fails((Status)7, f => f.IsInEnum(), "Must be one of the defined values.");
        Fails((Status)7, f => f.IsInEnum("Unknown status."), "Unknown status.");
        Passes<Status?>(Status.Draft, f => f.IsInEnum());
        Fails<Status?>((Status)7, f => f.IsInEnum(), "Must be one of the defined values.");
        Passes<Status?>(null, f => f.IsInEnum());
    }

    [TestMethod]
    public void Combined_flags_are_not_a_named_value()
    {
        Passes(Access.Write, f => f.IsInEnum());
        Fails(Access.Read | Access.Write, f => f.IsInEnum(), "Must be one of the defined values.");
    }
}
