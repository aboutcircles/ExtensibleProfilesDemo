namespace Circles.Profiles.Interfaces;

/// <summary>
/// Produces a 65-byte signature for canonical payload bytes and exposes the
/// effective <see cref="Address"/> that will go into CustomDataLink.SignerAddress.
/// Implementations cover both EOAs and contract wallets (e.g., Gnosis Safe).
/// </summary>
public interface ISigner
{
    /// <summary>
    /// Address whose signature rules are used by verifiers.
    /// For EOAs this is the EOA address; for Safes this is the Safe address.
    /// </summary>
    string Address { get; }

    /// <summary>
    /// Sign the canonical payload bytes for a given chain.
    /// EOAs usually sign keccak(payload); Safes sign the EIP-712 SafeMessage hash.
    /// Returns the canonical 65-byte signature (r||s||v).
    /// </summary>
    Task<byte[]> SignAsync(
        ReadOnlyMemory<byte> canonicalPayload,
        long chainId,
        CancellationToken ct = default);
}
