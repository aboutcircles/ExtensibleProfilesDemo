using Circles.Profiles.Models.Core;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk.Utils;

public static class SigningKeyUtils
{
    /// <summary>Returns the lowercase hex SHA‑3 (Keccak‑256) of the uncompressed pub‑key (64 bytes).</summary>
    public static string ComputeFingerprint(EthECKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        // EthECKey.GetPubKey(false) → 65 bytes (0x04 || X || Y)
        byte[] uncompressed = key.GetPubKey();
        byte[] raw = uncompressed.Skip(1).ToArray(); // drop 0x04
        byte[] fp = Sha3.Keccak256Bytes(raw); // 32 bytes

        return "0x" + fp.ToHex();
    }

    /// <summary>
    /// True ↔ <paramref name="fingerprint"/> is found in <paramref name="profile"/> 
    /// and covers <paramref name="unixTime"/>.
    /// </summary>
    public static bool IsFingerprintValid(string fingerprint, Profile profile, long unixTime)
    {
        if (!profile.SigningKeys.TryGetValue(fingerprint, out var meta))
            return false;

        bool notBefore = unixTime >= meta.ValidFrom;
        bool notAfter = meta.ValidTo == null || unixTime < meta.ValidTo;
        bool notRevoked = meta.RevokedAt == null || unixTime < meta.RevokedAt;

        return notBefore && notAfter && notRevoked;
    }
}