using System.Collections.ObjectModel;
using static ValidatingKit.UnitTests.RuleAssert;

namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class CollectionRulesTests
{
    private static IEnumerable<int> Lazy(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return i;
        }
    }

    [TestMethod]
    public void Not_empty_fails_null_and_empty_collections()
    {
        Fails<int[]>(null, f => f.NotEmpty(), "Must not be empty.");
        Fails<int[]>([], f => f.NotEmpty(), "Must not be empty.");
        Fails(new List<int>(), f => f.NotEmpty("Add one."), "Add one.");
        Fails(Lazy(0), f => f.NotEmpty(), "Must not be empty.");
        Passes<int[]>([1], f => f.NotEmpty());
        Passes(Lazy(1), f => f.NotEmpty());
        Passes(new HashSet<int> { 1 }, f => f.NotEmpty());
    }

    [TestMethod]
    public void Min_and_max_count()
    {
        Passes<int[]>([1, 2], f => f.MinLength(2).MaxLength(2));
        Fails<int[]>([1], f => f.MinLength(2), "Must have at least 2 items.");
        Fails<int[]>([], f => f.MinLength(1), "Must have at least 1 item.");
        Fails<int[]>([1, 2, 3], f => f.MaxLength(2), "Must have at most 2 items.");
        Fails(Lazy(3), f => f.MaxLength(2), "Must have at most 2 items.");
        Passes(Lazy(2), f => f.MaxLength(2).MinLength(2));
        Passes(Lazy(5), f => f.MaxLength(int.MaxValue));
        Fails<int[]>([1], f => f.MinLength(2, "More."), "More.");
        Fails<int[]>([1, 2, 3], f => f.MaxLength(2, "Fewer."), "Fewer.");
        Passes<int[]>(null, f => f.MinLength(2).MaxLength(0));
    }

    [TestMethod]
    public void Exact_and_ranged_count()
    {
        Passes<int[]>([1, 2], f => f.Length(2));
        Fails<int[]>([1, 2, 3], f => f.Length(2), "Must have exactly 2 items.");
        Fails(Lazy(3), f => f.Length(2), "Must have exactly 2 items.");
        Fails<int[]>([1], f => f.Length(2, "Two."), "Two.");
        Passes(Lazy(3), f => f.Length(2, 3));
        Fails(Lazy(4), f => f.Length(2, 3), "Must have between 2 and 3 items.");
        Fails<int[]>([1], f => f.Length(2, 3), "Must have between 2 and 3 items.");
        Fails<int[]>([1], f => f.Length(2, 3, "Some."), "Some.");
        Passes<int[]>(null, f => f.Length(2).Length(2, 3));
    }

    [TestMethod]
    public void Each_reports_elements_by_index()
    {
        var order = new Order { Tags = ["a", " ", "c", ""] };

        var error = Validation.That(order, "c").Field(o => o.Tags, f => f.NotEmpty().Each(t => t.NotBlank())).ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "tags[1]", "tags[3]" }, error.FieldErrors!.Keys.ToArray());
        Assert.AreEqual("tags[1]: Must not be blank. tags[3]: Must not be blank.", error.Message);
    }

    [TestMethod]
    public void Each_stops_at_the_first_failure_per_element()
    {
        string[] tags = ["", "toolong"];

        var error = Validation.That(new Order { Tags = tags }, "c")
            .Field(o => o.Tags, f => f.Each(t => t.NotBlank().MaxLength(3)))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "Must not be blank." }, error.FieldErrors!["tags[0]"]);
        CollectionAssert.AreEqual(new[] { "Must be at most 3 characters long." }, error.FieldErrors["tags[1]"]);
    }

    [TestMethod]
    public void Each_marks_the_collection_failed()
    {
        var error = Validation.That(new Order { Tags = [""] }, "c")
            .Field(o => o.Tags, f => f.Each(t => t.NotBlank()).Must(_ => false, "Unreached."))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "tags[0]" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public void Each_is_skipped_for_a_null_collection_or_after_a_failure()
    {
        Passes<string[]>(null, f => f.Each(t => t.NotBlank()));
        Fails<string[]>([], f => f.NotEmpty().Each(t => t.NotBlank()), "Must not be empty.");
    }

    [TestMethod]
    public void Each_works_for_every_collection_shape()
    {
        static string[] Paths<TCollection>(TCollection items, Func<Field<TCollection>, Field<TCollection>> rules) =>
            [.. Validation.That(new Tuple<TCollection>(items), "c")
                .Field(t => t.Item1, rules)
                .ToResult().Error!.FieldErrors!.Keys];

        string?[] expected = ["item1[1]"];

        CollectionAssert.AreEqual(expected, Paths<int[]>([1, 0], f => f.Each(i => i.GreaterThan(0))));
        CollectionAssert.AreEqual(expected, Paths<List<int>>([1, 0], f => f.Each(i => i.GreaterThan(0))));
        CollectionAssert.AreEqual(expected, Paths<IList<int>>([1, 0], f => f.Each(i => i.GreaterThan(0))));
        CollectionAssert.AreEqual(expected, Paths<IReadOnlyList<int>>(new ReadOnlyCollection<int>([1, 0]), f => f.Each(i => i.GreaterThan(0))));
        CollectionAssert.AreEqual(expected, Paths<ICollection<int>>(new HashSet<int> { 1, 0 }, f => f.Each(i => i.GreaterThan(0))));
        CollectionAssert.AreEqual(expected, Paths<IReadOnlyCollection<int>>(new HashSet<int> { 1, 0 }, f => f.Each(i => i.GreaterThan(0))));
        CollectionAssert.AreEqual(expected, Paths(Lazy(2).Select(i => 1 - i), f => f.Each(i => i.GreaterThan(0))));
    }

    [TestMethod]
    public void Each_validates_nested_elements_with_a_validator()
    {
        var order = new Order { Lines = [new Line("A-1", 1), new Line("", 0), new Line("C-3", -1)] };

        var error = Validation.That(order, "order.invalid")
            .Field(o => o.Lines, f => f.Required().Each(l => l.Valid(new LineValidator())))
            .ToResult().Error!;

        Assert.AreEqual("order.invalid", error.Code);
        Assert.AreEqual(
            "lines[1].sku: Must not be blank. lines[1].quantity: Must be greater than 0. lines[2].quantity: Must be greater than 0.",
            error.Message);
    }

    [TestMethod]
    public void Each_validates_nested_elements_inline()
    {
        var order = new Order { Lines = [new Line("", 1)] };

        var error = Validation.That(order, "c")
            .Field(o => o.Lines, f => f.Each(l => l.Valid(line => line.Field(x => x.Sku, s => s.NotBlank()))))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "lines[0].sku" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public void Each_reports_null_elements_with_required()
    {
        var order = new Order { Lines = [new Line("A", 1), null!] };

        var error = Validation.That(order, "c")
            .Field(o => o.Lines, f => f.Each(l => l.Required().Valid(new LineValidator())))
            .ToResult().Error!;

        Assert.AreEqual("lines[1]: Is required.", error.Message);
    }

    [TestMethod]
    public void Nested_collections_index_each_level()
    {
        var grid = new Tuple<List<List<int>>>([[1, 2], [3, -4]]);

        var error = Validation.That(grid, "c")
            .Field("rows", g => g.Item1, f => f.Each(row => row.Each(cell => cell.GreaterThan(0))))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "rows[1][1]" }, error.FieldErrors!.Keys.ToArray());
    }

    [TestMethod]
    public void A_validated_collection_indexes_from_the_root()
    {
        var error = Validation.That(new[] { "a", "" }, "c", "tags")
            .Field(t => t, f => f.Each(t => t.NotBlank()))
            .ToResult().Error!;

        CollectionAssert.AreEqual(new[] { "[1]" }, error.FieldErrors!.Keys.ToArray());
        Assert.AreEqual("[1]: Must not be blank.", error.Message);
    }
}
