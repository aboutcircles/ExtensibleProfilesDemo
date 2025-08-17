namespace Circles.Profiles.Interfaces;

public interface ISafeBytesVerifier
{
    /// <summary>
    /// Verifies signatures for ERC-1271 implementations that accept the RAW canonical
    /// payload bytes (not a bytes32 hash). For EOAs, callers should verify over
    /// keccak(payloadBytes).
    /// </summary>
    Task<bool> Verify1271WithBytesAsync(
        byte[] payloadBytes,
        string signerAddress,
        byte[] signature,
        CancellationToken ct = default);
}