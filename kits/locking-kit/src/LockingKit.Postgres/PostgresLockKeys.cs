using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LockingKit.Postgres;

/// <summary>
/// How lock keys become PostgreSQL advisory lock keys. The mapping is part of the contract and never changes, so
/// other tools — a SQL script, a service in another language — can take the same lock.
/// </summary>
public static class PostgresLockKeys
{
    /// <summary>
    /// The same mapping in SQL, with the key as <c>$1</c>: <c>pg_try_advisory_lock(</c>this<c>)</c> takes the lock
    /// LockingKit takes for that key. Needs PostgreSQL 11 or later for <c>sha256</c>.
    /// </summary>
    public const string SqlExpression = "('x' || left(encode(sha256(convert_to($1, 'UTF8')), 'hex'), 16))::bit(64)::bigint";

    /// <summary>
    /// The advisory lock key for <paramref name="key"/>: the first 8 bytes of the SHA-256 of its UTF-8 encoding, read
    /// as a big-endian signed 64-bit integer.
    /// </summary>
    /// <remarks>
    /// Distinct keys collide with probability about n² / 2⁶⁵ for n keys in use at once. A collision only makes two keys
    /// exclude each other; it never lets two holders share one key. LockingKit uses the single-<c>bigint</c> key space,
    /// which PostgreSQL keeps apart from the two-<c>int</c> one.
    /// </remarks>
    /// <param name="key">The lock key.</param>
    public static long ToAdvisoryKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
