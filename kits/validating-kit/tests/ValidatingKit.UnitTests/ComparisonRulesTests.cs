using static ValidatingKit.UnitTests.RuleAssert;

namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class ComparisonRulesTests
{
    [TestMethod]
    public void Greater_than()
    {
        Passes(2, f => f.GreaterThan(1));
        Fails(1, f => f.GreaterThan(1), "Must be greater than 1.");
        Fails(1, f => f.GreaterThan(1, "Bigger."), "Bigger.");
        Passes<int?>(2, f => f.GreaterThan(1));
        Fails<int?>(1, f => f.GreaterThan(1), "Must be greater than 1.");
        Passes<int?>(null, f => f.GreaterThan(1));
    }

    [TestMethod]
    public void Greater_than_or_equal_to()
    {
        Passes(1, f => f.GreaterThanOrEqualTo(1));
        Fails(0, f => f.GreaterThanOrEqualTo(1), "Must be greater than or equal to 1.");
        Fails(0, f => f.GreaterThanOrEqualTo(1, "At least one."), "At least one.");
        Passes<long?>(1, f => f.GreaterThanOrEqualTo(1L));
        Fails<long?>(0, f => f.GreaterThanOrEqualTo(1L), "Must be greater than or equal to 1.");
        Passes<long?>(null, f => f.GreaterThanOrEqualTo(1L));
    }

    [TestMethod]
    public void Less_than()
    {
        Passes(0.5m, f => f.LessThan(1m));
        Fails(1m, f => f.LessThan(1m), "Must be less than 1.");
        Fails(1m, f => f.LessThan(1m, "Smaller."), "Smaller.");
        Passes<decimal?>(0.5m, f => f.LessThan(1m));
        Fails<decimal?>(1.5m, f => f.LessThan(1m), "Must be less than 1.");
        Passes<decimal?>(null, f => f.LessThan(1m));
    }

    [TestMethod]
    public void Less_than_or_equal_to()
    {
        Passes(1.0, f => f.LessThanOrEqualTo(1.0));
        Fails(1.5, f => f.LessThanOrEqualTo(1.0), "Must be less than or equal to 1.");
        Fails(1.5, f => f.LessThanOrEqualTo(1.0, "At most one."), "At most one.");
        Passes<double?>(1.0, f => f.LessThanOrEqualTo(1.0));
        Fails<double?>(1.5, f => f.LessThanOrEqualTo(1.0), "Must be less than or equal to 1.");
        Passes<double?>(null, f => f.LessThanOrEqualTo(1.0));
    }

    [TestMethod]
    public void Between_is_inclusive()
    {
        Passes(1, f => f.Between(1, 3));
        Passes(3, f => f.Between(1, 3));
        Fails(0, f => f.Between(1, 3), "Must be between 1 and 3.");
        Fails(4, f => f.Between(1, 3), "Must be between 1 and 3.");
        Fails(4, f => f.Between(1, 3, "One to three."), "One to three.");
        Passes<int?>(2, f => f.Between(1, 3));
        Fails<int?>(4, f => f.Between(1, 3), "Must be between 1 and 3.");
        Fails<int?>(0, f => f.Between(1, 3), "Must be between 1 and 3.");
        Passes<int?>(null, f => f.Between(1, 3));
    }

    [TestMethod]
    public void Dates_and_times_compare_and_format_as_iso_8601()
    {
        var day = new DateOnly(2026, 10, 21);
        var instant = new DateTimeOffset(2026, 10, 21, 9, 30, 0, TimeSpan.Zero);

        Passes(day.AddDays(1), f => f.GreaterThan(day));
        Fails(day, f => f.GreaterThan(day), "Must be greater than 2026-10-21.");
        Fails(instant, f => f.LessThan(instant), "Must be less than 2026-10-21T09:30:00.0000000+00:00.");
        Fails(new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), f => f.LessThan(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), "Must be less than 2026-01-01T00:00:00.0000000Z.");
        Fails(new TimeOnly(8, 0), f => f.GreaterThan(new TimeOnly(9, 0)), "Must be greater than 09:00:00.0000000.");
        Fails<DateOnly?>(day, f => f.Between(day.AddDays(1), day.AddDays(2)), "Must be between 2026-10-22 and 2026-10-23.");
        Fails(TimeSpan.FromMinutes(5), f => f.LessThanOrEqualTo(TimeSpan.FromMinutes(1)), "Must be less than or equal to 00:01:00.");
    }

    [TestMethod]
    public void Reference_comparables_pass_null()
    {
        Passes<string>(null, f => f.GreaterThan("b"));
        Fails("a", f => f.GreaterThan("b"), "Must be greater than 'b'.");
        Fails(new Version(1, 0), f => f.GreaterThanOrEqualTo(new Version(2, 0)), "Must be greater than or equal to 2.0.");
    }
}
