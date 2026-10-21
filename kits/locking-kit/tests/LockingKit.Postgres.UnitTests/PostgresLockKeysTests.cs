using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LockingKit.Postgres.UnitTests;

[TestClass]
public sealed class PostgresLockKeysTests
{
    [TestMethod]
    public void The_advisory_key_is_the_first_eight_bytes_of_the_sha256_big_endian()
    {
        foreach (var key in new[] { "orders/42", "", "café 🔒", new string('x', 512) })
        {
            var expected = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            Assert.AreEqual(expected, PostgresLockKeys.ToAdvisoryKey(key), key);
        }
    }

    [TestMethod]
    [DataRow("orders/42", -4261225075031449721L)]
    [DataRow("jobs/nightly", -7836533424575595787L)]
    [DataRow("café 🔒", -2026654779180535945L)]
    public void Keys_map_to_values_that_never_change_between_versions(string key, long expected)
    {
        // Pinned: changing the mapping would let an old and a new deployment hold the same lock at once.
        Assert.AreEqual(expected, PostgresLockKeys.ToAdvisoryKey(key));
    }

    [TestMethod]
    public void Keys_are_case_sensitive_like_everywhere_else()
    {
        Assert.AreNotEqual(PostgresLockKeys.ToAdvisoryKey("Orders/42"), PostgresLockKeys.ToAdvisoryKey("orders/42"));
    }
}
