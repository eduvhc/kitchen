namespace LockingKit.Azure.UnitTests;

[TestClass]
public sealed class BlobLockNamesTests
{
    [TestMethod]
    [DataRow("orders/42", "locks/orders/42")]
    [DataRow("tenant-7/jobs/nightly_export", "locks/tenant-7/jobs/nightly_export")]
    [DataRow("v1.2/report.pdf", "locks/v1.2/report.pdf")]
    [DataRow("Orders/42", "locks/Orders/42")]
    public void Readable_keys_pass_through_so_guards_stay_recognisable(string key, string expected)
    {
        Assert.AreEqual(expected, BlobLockNames.ToBlobName("locks/", key));
    }

    [TestMethod]
    [DataRow("/leading", "~2Fleading")]
    [DataRow("trailing/", "trailing~2F")]
    [DataRow("a//b", "a/~2Fb")]
    [DataRow(".", "~2E")]
    [DataRow("..", "~2E~2E")]
    [DataRow("a/../b", "a/~2E./b")]
    [DataRow("a/./b", "a/~2E/b")]
    [DataRow("ends.", "ends~2E")]
    [DataRow(".hidden", "~2Ehidden")]
    [DataRow(@"a\b", "a~5Cb")]
    [DataRow("a b", "a~20b")]
    [DataRow("a~2Fb", "a~7E2Fb")]
    [DataRow("100%", "100~25")]
    [DataRow("café", "caf~C3~A9")]
    [DataRow("🔒", "~F0~9F~94~92")]
    [DataRow("tab\there", "tab~09here")]
    public void Characters_Azure_would_rewrite_or_reject_are_escaped(string key, string expected)
    {
        Assert.AreEqual(expected, BlobLockNames.Escape(key));
        Assert.AreEqual(key, BlobLockNames.Unescape(BlobLockNames.Escape(key)));
    }

    [TestMethod]
    public void The_mapping_is_injective_so_different_keys_never_share_a_guard()
    {
        var keys = new[]
        {
            "a/b", "a~2Fb", "a%2Fb", "a//b", "a/~2Fb", "/a/b", "a/b/", "A/b", "a/b ", "a/b.", "a/b~2E", "a.b", "a~2Eb",
            ".", "..", "~2E", "~", "~~", "~7E",
        };

        var names = keys.Select(k => BlobLockNames.ToBlobName("locks/", k)).ToList();

        Assert.AreEqual(keys.Length, names.Distinct(StringComparer.Ordinal).Count(), string.Join(" | ", names));
    }

    [TestMethod]
    public void Random_keys_round_trip_and_never_collide()
    {
        var random = new Random(48);
        var alphabet = "ab/.~-_ %\\é🔒\t".EnumerateRunes().Select(r => r.ToString()).ToArray();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < 20_000; i++)
        {
            var key = string.Concat(Enumerable.Range(0, random.Next(1, 12)).Select(_ => alphabet[random.Next(alphabet.Length)]));
            var name = BlobLockNames.Escape(key);

            Assert.AreEqual(key, BlobLockNames.Unescape(name));
            Assert.IsFalse(name.StartsWith('/') || name.EndsWith('/') || name.EndsWith('.') || name.Contains("//", StringComparison.Ordinal), name);
            Assert.IsFalse(name.Split('/').Any(s => s is "." or ".."), name);

            if (seen.TryGetValue(name, out var other))
            {
                Assert.AreEqual(other, key, $"'{other}' and '{key}' both map to '{name}'.");
            }

            seen[name] = key;
        }
    }

    [TestMethod]
    public void Names_too_long_for_Azure_fall_back_to_a_hash_in_their_own_namespace()
    {
        var longKey = new string('é', DistributedLocks.MaxKeyLength);
        var manySegments = string.Join('/', Enumerable.Repeat("s", 300));

        var hashed = BlobLockNames.ToBlobName("locks/", longKey);
        var segmented = BlobLockNames.ToBlobName("locks/", manySegments);

        StringAssert.StartsWith(hashed, "locks/~sha256~");
        Assert.AreEqual("locks/~sha256~".Length + 64, hashed.Length);
        StringAssert.StartsWith(segmented, "locks/~sha256~");
        Assert.AreNotEqual(hashed, segmented);
        Assert.AreEqual(hashed, BlobLockNames.ToBlobName("locks/", longKey), "Stable.");

        // A short key can never produce the hashed form: escapes are always ~ followed by two hex digits.
        Assert.AreEqual("locks/~7Esha256~7Eabc", BlobLockNames.ToBlobName("locks/", "~sha256~abc"));
    }

    [TestMethod]
    [DataRow("", true)]
    [DataRow("locks/", true)]
    [DataRow("app/locks/", true)]
    [DataRow("locks", false)]
    [DataRow("/locks/", false)]
    [DataRow("a//b/", false)]
    [DataRow("../", false)]
    [DataRow("my locks/", false)]
    [DataRow(null, false)]
    public void Prefixes_must_be_clean_folders(string? prefix, bool valid)
    {
        Assert.AreEqual(valid, BlobLockNames.IsValidPrefix(prefix));
    }
}
