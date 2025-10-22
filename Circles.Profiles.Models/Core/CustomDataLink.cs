using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Core;

/// <summary>Envelope that is signed and stored on IPFS.</summary>
public sealed record CustomDataLink
{
    [JsonPropertyName("@context")] public string Context { get; init; } = JsonLdMeta.LinkContext;

    [JsonPropertyName("@type")] public string Type { get; init; } = "CustomDataLink";

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("cid")] public string Cid { get; init; } = string.Empty;
    [JsonPropertyName("encrypted")] public bool Encrypted { get; init; }

    [JsonPropertyName("encryptionAlgorithm")]
    public string? EncryptionAlgorithm { get; init; }

    [JsonPropertyName("encryptionKeyFingerprint")]
    public string? EncryptionKeyFingerprint { get; init; }

    /* ─── replay-protection ────────────────────────── */
    [JsonPropertyName("chainId")] public long ChainId { get; init; }

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
}