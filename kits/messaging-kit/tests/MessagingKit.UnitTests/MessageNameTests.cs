namespace MessagingKit.UnitTests;

[TestClass]
public class MessageNameTests
{
    private sealed record SendEmail(string To);

    private sealed record HTTPRequest(string Url);

    private sealed record Invoice(Guid Id);

    [Message("email.send.v2")]
    private sealed record PinnedMessage(string To);

    [TestMethod]
    public void Kebab_cases_a_pascal_type_name()
    {
        Assert.AreEqual("send-email", MessageName.For<SendEmail>());
    }

    [TestMethod]
    public void Keeps_a_single_word_intact()
    {
        Assert.AreEqual("invoice", MessageName.For<Invoice>());
    }

    [TestMethod]
    public void Splits_an_acronym_from_the_word_that_follows_it()
    {
        Assert.AreEqual("http-request", MessageName.For<HTTPRequest>());
    }

    [TestMethod]
    public void Prefers_the_attribute_over_the_convention()
    {
        Assert.AreEqual("email.send.v2", MessageName.For<PinnedMessage>());
    }

    [TestMethod]
    public void Registry_falls_back_to_the_convention_for_unregistered_types()
    {
        var registry = new MessageTypeRegistry();

        Assert.AreEqual("send-email", registry.NameOf(typeof(SendEmail)));
    }

    [TestMethod]
    public void Registry_prefers_an_explicit_registration()
    {
        var registry = new MessageTypeRegistry();
        registry.Register("legacy-send", typeof(SendEmail));

        Assert.AreEqual("legacy-send", registry.NameOf(typeof(SendEmail)));
    }
}
