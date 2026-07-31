using MailingKit.Templating;

namespace MailingKit.Tests;

[TestClass]
public class ScribanTemplateRendererTests
{
    private readonly ScribanTemplateRenderer _renderer = new();

    [TestMethod]
    public void Renders_scalar_values()
    {
        var output = _renderer.Render("Hi {{ name }}", new Dictionary<string, object?> { ["name"] = "Ada" });
        Assert.AreEqual("Hi Ada", output);
    }

    [TestMethod]
    public void Renders_loops_over_collections()
    {
        var output = _renderer.Render(
            "{{ for item in items }}{{ item }};{{ end }}",
            new Dictionary<string, object?> { ["items"] = new[] { "a", "b" } });

        Assert.AreEqual("a;b;", output);
    }

    [TestMethod]
    public void Leaves_missing_variables_empty()
    {
        var output = _renderer.Render("Hi {{ name }}", new Dictionary<string, object?>());
        Assert.AreEqual("Hi ", output);
    }

    [TestMethod]
    public void Throws_on_a_malformed_template()
    {
        Assert.ThrowsExactly<TemplateRenderException>(
            () => _renderer.Render("{{ if }}", new Dictionary<string, object?>()));
    }
}
