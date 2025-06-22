using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ExtensibleProfilesDemo.Model;

public sealed record CustomDataLink
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("cid")] public string Cid { get; init; } = string.Empty;
    [JsonPropertyName("encrypted")] public bool Encrypted { get; init; }

    [JsonPropertyName("encryptionAlgorithm")]
    public string? EncryptionAlgorithm { get; init; }

    [JsonPropertyName("encryptionKeyFingerprint")]
    public string? EncryptionKeyFingerprint { get; init; }

    [JsonPropertyName("signerAddress")] public string SignerAddress { get; init; } = string.Empty;
    [JsonPropertyName("signedAt")] public long SignedAt { get; init; }
    [JsonPropertyName("nonce")] public string Nonce { get; init; } = string.Empty;
    [JsonPropertyName("signature")] public string Signature { get; init; } = string.Empty;

    public static string NewNonce()
    {
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);
        return "0x" + Convert.ToHexString(buf).ToLowerInvariant();
    }

    public byte[] CanonicaliseForSigning() => CanonicalJson.CanonicaliseWithoutSignature(this);
}