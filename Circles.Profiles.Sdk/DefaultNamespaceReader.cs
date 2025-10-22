using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Utils;
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
        {
            if (l.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase))
            {
                return l;
            }
        }

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
                if (l.SignedAt <= newerThanUnixTs)
                {
                    continue;
                }

                if (!await Verify(l, ct))
                {
                    continue;
                }

                yield return l;
            }

            cur = chunk.Prev;
        }
    }

    private async Task<bool> Verify(CustomDataLink l, CancellationToken ct)
    {
        bool seenBefore = NonceRegistrySingleton.Instance.SeenBefore(l.Nonce);
        if (seenBefore)
        {
            return false; // replay → drop
        }

        // Compute canonical payload bytes once for both paths.
        byte[] payloadBytes = CanonicalJson.CanonicaliseWithoutSignature(l);
        byte[] payloadHash = Sha3.Keccak256Bytes(payloadBytes);

        // Primary path (works for EOAs and many contracts):
        bool ok = await _verifier.VerifyAsync(
            payloadHash,
            l.SignerAddress,
            l.Signature.HexToByteArray(),
            ct);

        if (ok)
        {
            return true;
        }

        // Secondary path (Safe messages):
        // Prefer a verifier that explicitly supports the "bytes" variant.
        if (_verifier is ISafeBytesVerifier safe)
        {
            return await safe.Verify1271WithBytesAsync(
                payloadBytes,
                l.SignerAddress,
                l.Signature.HexToByteArray(),
                ct);
        }

        // Back-compat: legacy DefaultSignatureVerifier also has this helper.
        if (_verifier is DefaultSignatureVerifier def)
        {
            return await def.Verify1271WithBytesAsync(
                payloadBytes,
                l.SignerAddress,
                l.Signature.HexToByteArray(),
                ct);
        }

        return false;
    }
}