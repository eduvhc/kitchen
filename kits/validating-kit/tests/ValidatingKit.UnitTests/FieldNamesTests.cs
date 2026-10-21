namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class FieldNamesTests
{
    [TestMethod]
    [DataRow("r => r.StorageId", "storageId")]
    [DataRow("static r => r.StorageId", "storageId")]
    [DataRow("(r) => r.StorageId", "storageId")]
    [DataRow("(Order o) => o.Name", "name")]
    [DataRow("x=>x.Name", "name")]
    [DataRow("x => x!.Name", "name")]
    [DataRow("x => x.Address!.Street", "address.street")]
    [DataRow("x => x.Address?.Street", "address.street")]
    [DataRow("x => x.URL", "url")]
    [DataRow("x => x.IOStream", "ioStream")]
    [DataRow("x => x.@class", "class")]
    [DataRow("x => x._raw", "_raw")]
    [DataRow("x => x", "")]
    public void Selectors_become_camel_case_paths(string expression, string expected)
    {
        Assert.AreEqual(expected, FieldNames.FromSelector(expression));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("selector")]
    [DataRow("x => y.Name")]
    [DataRow("x => x.Name.Trim()")]
    [DataRow("x => x.Items[0]")]
    [DataRow("x => x.A + x.B")]
    [DataRow("(a, b) => a.Name")]
    public void Other_selectors_are_rejected(string expression)
    {
        Assert.ThrowsExactly<ArgumentException>(() => FieldNames.FromSelector(expression));
    }

    [TestMethod]
    public void Names_are_cached()
    {
        Assert.AreSame(FieldNames.FromSelector("q => q.CachedName"), FieldNames.FromSelector("q => q.CachedName"));
    }

    [TestMethod]
    [DataRow("request", "request")]
    [DataRow("command.Body", "command.Body")]
    [DataRow(" request ", "request")]
    [DataRow("new Order()", "value")]
    [DataRow("Load(id)", "value")]
    [DataRow("", "value")]
    public void Root_names_are_plain_names_or_value(string expression, string expected)
    {
        Assert.AreEqual(expected, FieldNames.ForRoot(expression));
    }

    [TestMethod]
    public void Type_names_drop_generic_arity()
    {
        Assert.AreEqual("deleteFileRequested", FieldNames.ForType(typeof(DeleteFileRequested)));
        Assert.AreEqual("list", FieldNames.ForType(typeof(List<int>)));
    }

    [TestMethod]
    [DataRow("", "a", "a")]
    [DataRow("a", "", "a")]
    [DataRow("a", "b", "a.b")]
    [DataRow("a", "[1]", "a[1]")]
    [DataRow("a", "[1].b", "a[1].b")]
    public void Paths_combine(string prefix, string path, string expected)
    {
        Assert.AreEqual(expected, FieldNames.Combine(prefix, path));
    }
}
