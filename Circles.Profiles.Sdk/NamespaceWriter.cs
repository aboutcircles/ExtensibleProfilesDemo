using System.Text;
using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Utils;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Writes to one <em>(ownerAvatar, namespaceKey)</em> log.
/// If the same <paramref name="logicalName"/> is written again
/// the newer link <b>replaces</b> the older entry <i>inside the head chunk</i>.
/// Across chunk rotations the profile‑level index ensures random‑access always
/// resolves to the most recent occurrence while older links stay in history.
/// </summary>
public sealed class NamespaceWriter : INamespaceWriter
{
    private readonly Profile _ownerProfile;
    private readonly string _nsKeyLower; // recipient‑addr lower‑cased
    private readonly IIpfsStore _ipfs;
    private readonly ILinkSigner _signer;

    private NameIndexDoc _index = new(); // hydrated in CreateAsync
    private NamespaceChunk _head = new(); // idem

    private NamespaceWriter(Profile ownerProfile,
        string namespaceKey,
        IIpfsStore ipfs,
        ILinkSigner signer)
    {
        _ownerProfile = ownerProfile;
        _nsKeyLower = namespaceKey.ToLowerInvariant();
        _ipfs = ipfs;
        _signer = signer;
    }

    /// <summary>
    /// Asynchronously loads the existing index/chunk state and returns a ready‑to‑use writer.
    /// </summary>
    public static async Task<NamespaceWriter> CreateAsync(
        Profile ownerProfile,
        string namespaceKey,
        IIpfsStore ipfs,
        ILinkSigner signer,
        CancellationToken ct = default)
    {
        var w = new NamespaceWriter(ownerProfile, namespaceKey, ipfs, signer);

        bool hasIndex = ownerProfile.Namespaces.TryGetValue(w._nsKeyLower, out var idxCid);
        if (hasIndex)
        {
            w._index = await Helpers.LoadIndex(idxCid, ipfs, ct);
            w._head = await Helpers.LoadChunk(w._index.Head, ipfs, ct);
        }

        return w;
    }

    /* ───────────── single helpers ────────────────────────────────────── */

    public async Task<CustomDataLink> AddJsonAsync(string name,
        string json,
        string pk,
        CancellationToken ct = default)
    {
        string cid = await _ipfs.AddStringAsync(json, pin: true, ct);
        return await AttachExistingCidAsync(name, cid, pk, ct);
    }

    public async Task<CustomDataLink> AttachExistingCidAsync(
        string name,
        string cid,
        string pk,
        CancellationToken ct = default)
    {
        bool nameEmpty = string.IsNullOrWhiteSpace(name);
        if (nameEmpty)
        {
            throw new ArgumentNullException(nameof(name));
        }

        bool cidEmpty = string.IsNullOrWhiteSpace(cid);
        if (cidEmpty)
        {
            throw new ArgumentNullException(nameof(cid));
        }

        bool pkEmpty = string.IsNullOrWhiteSpace(pk);
        if (pkEmpty)
        {
            throw new ArgumentNullException(nameof(pk));
        }

        var draft = new CustomDataLink
        {
            Name = name,
            Cid = cid,
            ChainId = Helpers.DefaultChainId,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = CustomDataLink.NewNonce(),
            Encrypted = false
        };

        CustomDataLink signed = _signer.Sign(draft, pk);

        await PersistAsync([signed], ct);
        return signed;
    }

    /* bulk -------------------------------------------------------- */

    public async Task<IReadOnlyList<CustomDataLink>> AddJsonBatchAsync(
        IEnumerable<(string name, string json)> items,
        string pk,
        CancellationToken ct = default)
    {
        bool pkEmpty = string.IsNullOrWhiteSpace(pk);
        if (pkEmpty)
        {
            throw new ArgumentNullException(nameof(pk));
        }

        var vec = new List<CustomDataLink>();

        var itemsArray = items.ToArray();

        bool anyNameMissing = itemsArray.Any(o => string.IsNullOrWhiteSpace(o.name));
        if (anyNameMissing)
        {
            throw new ArgumentException("At least one of the items in the list doesn't have a name.", nameof(items));
        }

        foreach (var (n, j) in itemsArray)
        {
            ct.ThrowIfCancellationRequested();
            string cid = await _ipfs.AddStringAsync(j, pin: true, ct);

            vec.Add(new CustomDataLink
            {
                Name = n,
                Cid = cid,
                ChainId = Helpers.DefaultChainId,
                SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = CustomDataLink.NewNonce(),
                Encrypted = false
            });
        }

        vec = vec.Select(l => _signer.Sign(l, pk)).ToList();
        await PersistAsync(vec, ct);
        return vec;
    }

    public async Task<IReadOnlyList<CustomDataLink>> AttachCidBatchAsync(
        IEnumerable<(string name, string cid)> items,
        string pk,
        CancellationToken ct = default)
    {
        bool pkEmpty = string.IsNullOrWhiteSpace(pk);
        if (pkEmpty)
        {
            throw new ArgumentNullException(nameof(pk));
        }

        var vec = items.Select(i =>
        {
            bool nameEmpty = string.IsNullOrWhiteSpace(i.name);
            if (nameEmpty)
            {
                throw new ArgumentNullException(nameof(i.name));
            }

            bool cidEmpty = string.IsNullOrWhiteSpace(i.cid);
            if (cidEmpty)
            {
                throw new ArgumentNullException(nameof(i.cid));
            }

            return new CustomDataLink
            {
                Name = i.name,
                Cid = i.cid,
                SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = CustomDataLink.NewNonce(),
                Encrypted = false
            };
        }).ToList();

        vec = vec.Select(l => _signer.Sign(l, pk)).ToList();
        await PersistAsync(vec, ct);
        return vec;
    }

    /* internals --------------------------------------------------- */

    private async Task PersistAsync(IEnumerable<CustomDataLink> newLinks, CancellationToken ct)
    {
        foreach (var link in newLinks)
        {
            ct.ThrowIfCancellationRequested();

            bool full = _head.Links.Count >= Helpers.ChunkMaxLinks;
            if (full)
            {
                string closedCid = await Helpers.SaveChunk(_head, _ipfs, ct);

                foreach (var l in _head.Links)
                {
                    _index.Entries[l.Name] = closedCid;
                }

                _head = new NamespaceChunk { Prev = closedCid };
            }

            int idx = _head.Links.FindIndex(l => l.Name.Equals(link.Name, StringComparison.OrdinalIgnoreCase));
            bool exists = idx >= 0;
            if (exists)
            {
                _head.Links[idx] = link;
            }
            else
            {
                _head.Links.Add(link);
            }
        }

        string headCid = await Helpers.SaveChunk(_head, _ipfs, ct);

        foreach (var l in _head.Links)
        {
            _index.Entries[l.Name] = headCid;
        }

        _index.Head = headCid;

        string indexJson = JsonSerializer.Serialize(_index, Models.JsonSerializerOptions.JsonLd);
        string indexCid = await _ipfs.CalcCidAsync(
            Encoding.UTF8.GetBytes(indexJson), ct);

        _ownerProfile.Namespaces[_nsKeyLower] = indexCid;

        await _ipfs.AddStringAsync(indexJson, pin: true, ct);
    }

    /// <summary>
    /// Inserts a <b>pre‑signed</b> link after validating
    ///   • its signature
    ///   • signerAddress == namespace key (this writer’s <c>_nsKeyLower</c>)
    /// </summary>
    public async Task<CustomDataLink> AcceptSignedLinkAsync(
        CustomDataLink signedLink,
        Profile operatorProfile,
        IChainApi chain,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signedLink);
        ArgumentNullException.ThrowIfNull(operatorProfile);

        bool sameNamespace = signedLink.SignerAddress.Equals(_nsKeyLower, StringComparison.OrdinalIgnoreCase);
        if (!sameNamespace)
        {
            throw new InvalidOperationException(
                $"link signer ({signedLink.SignerAddress}) must equal namespace key ({_nsKeyLower})");
        }

        var verifier = new DefaultSignatureVerifier(chain);

        byte[] payload = CanonicalJson.CanonicaliseWithoutSignature(signedLink);
        byte[] hash = Sha3.Keccak256Bytes(payload);
        byte[] sig = signedLink.Signature.HexToByteArray();

        bool primaryOk = await verifier.VerifyAsync(
            hash,
            signedLink.SignerAddress,
            sig,
            ct).ConfigureAwait(false);

        bool needsBytesFallback = !primaryOk;
        if (needsBytesFallback)
        {
            bool bytesOk = await verifier.Verify1271WithBytesAsync(
                payload,
                signedLink.SignerAddress,
                sig,
                ct).ConfigureAwait(false);

            if (!bytesOk)
            {
                throw new InvalidOperationException("invalid signature on supplied link");
            }
        }

        string code = await chain.GetCodeAsync(signedLink.SignerAddress, ct).ConfigureAwait(false);
        bool isEoa = string.Equals(code, "0x", StringComparison.OrdinalIgnoreCase);
        if (isEoa)
        {
            var sigObj = Nethereum.Signer.EthECDSASignatureFactory.ExtractECDSASignature(signedLink.Signature);
            var rec = Nethereum.Signer.EthECKey.RecoverFromSignature(sigObj, hash)
                      ?? throw new InvalidOperationException("failed to recover public key");

            string fp = SigningKeyUtils.ComputeFingerprint(rec);
            bool keyValid = SigningKeyUtils.IsFingerprintValid(fp, operatorProfile, signedLink.SignedAt);
            if (!keyValid)
            {
                throw new InvalidOperationException("signing key is expired / revoked");
            }
        }

        await PersistAsync([signedLink], ct).ConfigureAwait(false);
        return signedLink;
    }
}