using System.Text.RegularExpressions;
using static ValidatingKit.UnitTests.RuleAssert;

namespace ValidatingKit.UnitTests;

[TestClass]
public sealed partial class StringRulesTests
{
    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex Lowercase();

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  \t")]
    public void Not_blank_fails_null_empty_and_white_space(string? value)
    {
        Fails(value, f => f.NotBlank(), "Must not be blank.");
        Fails(value, f => f.NotBlank("Name it."), "Name it.");
    }

    [TestMethod]
    public void Not_blank_passes_text()
    {
        Passes(" a ", f => f.NotBlank());
    }

    [TestMethod]
    public void Not_empty_fails_null_and_empty_but_not_white_space()
    {
        Fails<string>(null, f => f.NotEmpty(), "Must not be empty.");
        Fails("", f => f.NotEmpty(), "Must not be empty.");
        Fails("", f => f.NotEmpty("Fill it."), "Fill it.");
        Passes(" ", f => f.NotEmpty());
    }

    [TestMethod]
    public void Min_length()
    {
        Passes("ab", f => f.MinLength(2));
        Fails("a", f => f.MinLength(2), "Must be at least 2 characters long.");
        Fails("", f => f.MinLength(1), "Must be at least 1 character long.");
        Fails("a", f => f.MinLength(2, "Short."), "Short.");
        Passes<string>(null, f => f.MinLength(2));
    }

    [TestMethod]
    public void Max_length()
    {
        Passes("ab", f => f.MaxLength(2));
        Fails("abc", f => f.MaxLength(2), "Must be at most 2 characters long.");
        Fails("abc", f => f.MaxLength(2, "Long."), "Long.");
        Passes<string>(null, f => f.MaxLength(2));
    }

    [TestMethod]
    public void Exact_length()
    {
        Passes("abc", f => f.Length(3));
        Fails("ab", f => f.Length(3), "Must be exactly 3 characters long.");
        Fails("abcd", f => f.Length(3), "Must be exactly 3 characters long.");
        Fails("ab", f => f.Length(3, "Three."), "Three.");
        Passes<string>(null, f => f.Length(3));
    }

    [TestMethod]
    public void Length_range_is_inclusive()
    {
        Passes("ab", f => f.Length(2, 3));
        Passes("abc", f => f.Length(2, 3));
        Fails("a", f => f.Length(2, 3), "Must be between 2 and 3 characters long.");
        Fails("abcd", f => f.Length(2, 3), "Must be between 2 and 3 characters long.");
        Fails("a", f => f.Length(2, 3, "Two or three."), "Two or three.");
        Passes<string>(null, f => f.Length(2, 3));
    }

    [TestMethod]
    public void Matches_a_pattern()
    {
        Passes("abc", f => f.Matches("^[a-z]+$"));
        Fails("aBc", f => f.Matches("^[a-z]+$"), "Is not in the correct format.");
        Fails("aBc", f => f.Matches("^[a-z]+$", "Lowercase."), "Lowercase.");
        Passes<string>(null, f => f.Matches("^[a-z]+$"));
    }

    [TestMethod]
    public void Matches_a_regex()
    {
        Passes("abc", f => f.Matches(Lowercase()));
        Fails("a1", f => f.Matches(Lowercase()), "Is not in the correct format.");
        Fails("a1", f => f.Matches(Lowercase(), "Lowercase."), "Lowercase.");
        Passes<string>(null, f => f.Matches(Lowercase()));
    }

    [TestMethod]
    public void A_pattern_that_times_out_does_not_match()
    {
        var slow = new Regex("^(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1));

        Fails(new string('a', 5000) + "!", f => f.Matches(slow), "Is not in the correct format.");
        Fails(new string('a', 5000) + "!", f => f.Matches("^(a+)+$"), "Is not in the correct format.");
    }

    [TestMethod]
    [DataRow("a@b")]
    [DataRow("first.last+tag@example.co.uk")]
    [DataRow("\"quoted\"@example.com")]
    public void Email_accepts_address_shapes(string value)
    {
        Passes(value, f => f.Email());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("plain")]
    [DataRow("@example.com")]
    [DataRow("user@")]
    [DataRow("a@b@c")]
    [DataRow("a b@example.com")]
    [DataRow("ab@example.com ")]
    [DataRow("ab@.example.com")]
    [DataRow("ab@example.com.")]
    [DataRow("ab\n@example.com")]
    public void Email_rejects_non_addresses(string value)
    {
        Fails(value, f => f.Email(), "Must be a valid email address.");
        Fails(value, f => f.Email("Mail."), "Mail.");
    }

    [TestMethod]
    public void Email_passes_null()
    {
        Passes<string>(null, f => f.Email());
    }
}
