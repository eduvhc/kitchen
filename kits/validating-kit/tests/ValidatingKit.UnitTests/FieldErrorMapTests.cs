namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class FieldErrorMapTests
{
    private static IReadOnlyDictionary<string, string[]> Map(int fields) =>
        Validation.That(Enumerable.Range(0, fields).Select(_ => "").ToArray(), "c")
            .Field(t => t, f => f.Each(t => t.NotBlank()))
            .Field("[0]", t => t[0], f => f.MinLength(1, "Again."))
            .ToResult().Error!.FieldErrors!;

    [TestMethod]
    [DataRow(3)]
    [DataRow(40)]
    public void Reads_like_a_dictionary(int fields)
    {
        var map = Map(fields);

        Assert.AreEqual(fields, map.Count);
        Assert.IsTrue(map.ContainsKey("[1]"));
        Assert.IsFalse(map.ContainsKey("[x]"));
        CollectionAssert.AreEqual(new[] { "Must not be blank.", "Again." }, map["[0]"]);
        Assert.IsTrue(map.TryGetValue("[2]", out var messages));
        CollectionAssert.AreEqual(new[] { "Must not be blank." }, messages);
        Assert.IsFalse(map.TryGetValue("[x]", out _));
        Assert.ThrowsExactly<KeyNotFoundException>(() => map["[x]"]);
        CollectionAssert.AreEqual(Enumerable.Range(0, fields).Select(i => $"[{i}]").ToArray(), map.Keys.ToArray());
        Assert.AreEqual(fields + 1, map.Values.Sum(v => v.Length));
        Assert.AreEqual(fields, map.ToList().Count);
        Assert.AreEqual(fields, ((System.Collections.IEnumerable)map).Cast<object>().Count());
    }

    [TestMethod]
    public void Maps_with_the_same_content_make_equal_errors()
    {
        Assert.AreEqual(
            Validation.That(new Order(), "c").Field(o => o.Name, f => f.NotBlank()).ToResult().Error,
            Error.Validation("c", "name: Must not be blank.", new Dictionary<string, string[]> { ["name"] = ["Must not be blank."] }));
    }
}
