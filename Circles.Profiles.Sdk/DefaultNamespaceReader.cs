using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Streams a namespace **with on‑the‑fly signature verification**.
/// Links failing verification are silently skipped.
/// </summary>
public sealed class DefaultNamespaceReader : INamespaceReader
{
    private readonly IIpfsStore _ipfs;
    private readonly ISignatureVerifier _verifier;
    private readonly string? _headCid;

    public DefaultNamespaceReader(string? headCid,
        IIpfsStore ipfs,
        ISignatureVerifier verifier)
    {
        _headCid = headCid;
        _ipfs = ipfs;
        _verifier = verifier;
    }

    public async Task<CustomDataLink?> GetLatestAsync(
        string logicalName, CancellationToken ct = default)
    {
        await foreach (var l in StreamAsync(0, ct))
            if (l.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }

    public async IAsyncEnumerable<CustomDataLink> StreamAsync(
        long newerThanUnixTs = 0,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        for (string? cur = _headCid; cur is not null;)
        {
            var chunk = await Helpers.LoadChunk(cur, _ipfs, ct);

            foreach (var l in chunk.Links.OrderByDescending(l => l.SignedAt))
            {
                if (l.SignedAt <= newerThanUnixTs) continue;
                if (!await Verify(l, ct)) continue;
                yield return l;
            }

            cur = chunk.Prev;
        }
    }

    private async Task<bool> Verify(CustomDataLink l, CancellationToken ct)
    {
        if (NonceRegistry.SeenBefore(l.Nonce))
        {
            return false; // replay → drop
        }

        byte[] hash = Sha3.Keccak256Bytes(CanonicalJson.CanonicaliseWithoutSignature(l));
        return await _verifier.VerifyAsync(hash, l.SignerAddress, l.Signature.HexToByteArray(), ct);
    }
}