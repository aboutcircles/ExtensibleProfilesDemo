using System.Numerics;

namespace Circles.Profiles.Interfaces;

/// <summary>
/// Result of an ERC‑1271 <c>isValidSignature</c> pre‑flight.
/// • <see cref="Reverted"/>   – the EVM reverted (i.e. “invalid signature”).  
/// • <see cref="ReturnData"/> – raw ABI‑encoded value (empty when reverted).
/// </summary>
public readonly record struct SignatureCallResult(bool Reverted, byte[] ReturnData);

/// <summary>
/// Minimal read‑only blockchain API needed by <see cref="ISignatureVerifier"/>.
/// </summary>
public interface IChainApi
{
    BigInteger Id { get; }
    
    /// <summary>Returns the contract code at <paramref name="address"/> as a 0x‑prefixed hex string.</summary>
    Task<string> GetCodeAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Executes the ERC‑1271 <c>isValidSignature</c> call via <c>eth_call</c>.
    /// Never throws on “execution reverted” – that is surfaced via <see cref="SignatureCallResult.Reverted"/>.
    /// </summary>
    Task<SignatureCallResult> CallIsValidSignatureAsync(
        string address,
        string abi,
        byte[] dataOrHash,
        byte[] signature,
        CancellationToken ct = default);
    
    Task<BigInteger> GetSafeNonceAsync(
        string safeAddress, CancellationToken ct = default);
}