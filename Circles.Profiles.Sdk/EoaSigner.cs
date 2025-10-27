using Circles.Profiles.Interfaces;
using Circles.Profiles.Sdk.Utils;
using Nethereum.Model;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk;

/// <summary>EOA signer: signs keccak(payloadBytes).</summary>
public sealed class EoaSigner : ISigner
{
    private readonly EthECKey _key;

    public EoaSigner(EthECKey key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public string Address => _key.GetPublicAddress();

    public Task<byte[]> SignAsync(
        ReadOnlyMemory<byte> canonicalPayload,
        long chainId,
        CancellationToken ct = default)
    {
        byte[] hash = Sha3.Keccak256Bytes(canonicalPayload.Span);
        var sig = _key.SignAndCalculateV(hash);
        byte[] bytes = sig.To64ByteArray().Concat([sig.V[0]]).ToArray();
        return Task.FromResult(bytes);
    }
}
