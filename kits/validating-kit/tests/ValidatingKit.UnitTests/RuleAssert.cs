namespace ValidatingKit.UnitTests;

/// <summary>Runs rules against one value, the way a <c>Field</c> call would, and reads back the outcome.</summary>
internal static class RuleAssert
{
    public static string? Check<TProp>(TProp? value, Func<Field<TProp>, Field<TProp>> rules)
    {
        var result = Validation.That(new Box<TProp>(value), "c").Field(b => b.Value, rules).ToResult();
        return result.IsSuccess ? null : result.Error.FieldErrors!["value"].Single();
    }

    public static void Passes<TProp>(TProp? value, Func<Field<TProp>, Field<TProp>> rules) =>
        Assert.IsNull(Check(value, rules), $"Expected {value ?? (object)"null"} to pass.");

    public static void Fails<TProp>(TProp? value, Func<Field<TProp>, Field<TProp>> rules, string message) =>
        Assert.AreEqual(message, Check(value, rules), $"Expected {value ?? (object)"null"} to fail.");

    private sealed record Box<TProp>(TProp? Value);
}
