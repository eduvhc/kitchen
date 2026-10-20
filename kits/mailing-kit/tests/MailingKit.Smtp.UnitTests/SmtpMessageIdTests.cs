using MailingKit.Transport;

namespace MailingKit.Smtp.UnitTests;

[TestClass]
public sealed class SmtpMessageIdTests
{
    private static OutgoingEmail Email(string? key, Dictionary<string, string>? headers = null) => new()
    {
        IdempotencyKey = key,
        FromAddress = "no-reply@example.com",
        To = ["ada@example.com"],
        Subject = "Hello",
        TextBody = "Hi",
        Headers = headers ?? [],
    };

    [TestMethod]
    public void Message_id_comes_from_the_idempotency_key_and_the_sender_domain()
    {
        var mime = SmtpEmailSender.Build(Email("mailingkit-0123abcd"));

        Assert.AreEqual("mailingkit-0123abcd@example.com", mime.MessageId);
    }

    [TestMethod]
    public void A_retry_builds_the_same_message_id()
    {
        var first = SmtpEmailSender.Build(Email("mailingkit-0123abcd"));
        var retry = SmtpEmailSender.Build(Email("mailingkit-0123abcd"));

        Assert.AreEqual(first.MessageId, retry.MessageId);
    }

    [TestMethod]
    public void Different_keys_build_different_message_ids()
    {
        var a = SmtpEmailSender.Build(Email("mailingkit-a"));
        var b = SmtpEmailSender.Build(Email("mailingkit-b"));

        Assert.AreNotEqual(a.MessageId, b.MessageId);
    }

    [TestMethod]
    public void Without_a_key_each_build_gets_a_fresh_message_id()
    {
        var a = SmtpEmailSender.Build(Email(null));
        var b = SmtpEmailSender.Build(Email(null));

        Assert.AreNotEqual(a.MessageId, b.MessageId);
    }

    [TestMethod]
    public void A_key_that_is_not_a_dot_atom_is_hashed_into_a_valid_id()
    {
        var id = SmtpEmailSender.DeterministicMessageId("order 42 <retry>", "example.com");

        Assert.AreEqual(id, SmtpEmailSender.DeterministicMessageId("order 42 <retry>", "example.com"));
        StringAssert.Matches(id, new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}@example[.]com$"));
    }

    [TestMethod]
    public void An_explicit_message_id_header_wins()
    {
        var mime = SmtpEmailSender.Build(Email("mailingkit-0123abcd", new() { ["Message-ID"] = "<mine@example.com>" }));

        Assert.AreNotEqual("mailingkit-0123abcd@example.com", mime.MessageId);
    }
}
