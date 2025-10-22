using Circles.Profiles.Sdk.Utils;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Verifies EOA ECDSA signatures over a 32-byte Keccak hash.
/// Enforces EIP-2 (low-S). No chain calls required.
/// </summary>
public sealed class EoaSignatureVerifier
{
    /// <summary>
    /// Synchronous verification over a 32-byte hash.
    /// </summary>
    public bool Verify(byte[] hash, string signerAddress, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(signature);

        if (string.IsNullOrWhiteSpace(signerAddress))
        {
            throw new ArgumentException("Empty address", nameof(signerAddress));
        }

        string sigHex = "0x" + signature.ToHex();
        var sig = EthECDSASignatureFactory.ExtractECDSASignature(sigHex);

        if (!sig.IsLowS) return false; // EIP-2

        string? recovered = EthECKey.RecoverFromSignature(sig, hash)?.GetPublicAddress();
        return recovered?.Equals(signerAddress, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Convenience for readers that only have the canonical payload bytes.
    /// EOAs sign the keccak of those bytes.
    /// </summary>
    public bool VerifyOverBytes(byte[] payloadBytes, string signerAddress, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        ArgumentNullException.ThrowIfNull(signature);

        byte[] h = Sha3.Keccak256Bytes(payloadBytes);
        return Verify(h, signerAddress, signature);
    }

    /* ─────────────────── async aliases to fit existing call sites ─────────────────── */

    /// <summary>
    /// Async alias for <see cref="Verify"/> to match existing facade calls.
    /// </summary>
    public Task<bool> VerifyAsync(
        byte[] hash,
        string signerAddress,
        byte[] signature,
        CancellationToken _ = default)
        => Task.FromResult(Verify(hash, signerAddress, signature));

    /// <summary>
    /// Async alias for <see cref="VerifyOverBytes"/> to match existing facade calls.
    /// Named to be compatible with older DefaultSignatureVerifier helper usage.
    /// </summary>
    public Task<bool> Verify1271WithBytesAsync(
        byte[] payloadBytes,
        string signerAddress,
        byte[] signature,
        CancellationToken _ = default)
        => Task.FromResult(VerifyOverBytes(payloadBytes, signerAddress, signature));
}
