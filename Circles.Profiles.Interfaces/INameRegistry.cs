namespace Circles.Sdk;

/// <summary>On-chain name-registry (avatar → profile CID).</summary>
public interface INameRegistry
{
    Task<string?> GetProfileCidAsync(string avatar, CancellationToken ct = default);
    Task<string?> UpdateProfileCidAsync(string avatar, byte[] metadataDigest32,
        CancellationToken ct = default);
}