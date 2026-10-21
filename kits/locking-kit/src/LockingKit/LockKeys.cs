using System.Text;
using ResultingKit;

namespace LockingKit;

/// <summary>
/// The provider-neutral key rules: any string a provider can map without loss. Providers map valid keys to what their
/// backend accepts (escaped blob names, hashed advisory keys), so callers never see backend rules.
/// </summary>
internal static class LockKeys
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Error? Validate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
        {
            return LockingErrors.BadKey("the key is empty.");
        }

        if (key.Length > DistributedLocks.MaxKeyLength)
        {
            return LockingErrors.BadKey($"it is longer than {DistributedLocks.MaxKeyLength} characters.");
        }

        try
        {
            StrictUtf8.GetByteCount(key);
        }
        catch (EncoderFallbackException)
        {
            return LockingErrors.BadKey("it is not valid Unicode.");
        }

        return null;
    }
}
