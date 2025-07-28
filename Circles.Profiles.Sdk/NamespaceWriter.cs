using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;

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

        if (ownerProfile.Namespaces.TryGetValue(w._nsKeyLower, out var idxCid))
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
        var cid = await _ipfs.AddJsonAsync(json, pin: true, ct);
        return await AttachExistingCidAsync(name, cid, pk, ct);
    }

    public async Task<CustomDataLink> AttachExistingCidAsync(
        string name,
        string cid,
        string pk,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrWhiteSpace(cid)) throw new ArgumentNullException(nameof(cid));
        if (string.IsNullOrWhiteSpace(pk)) throw new ArgumentNullException(nameof(pk));

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
        if (string.IsNullOrWhiteSpace(pk))
            throw new ArgumentNullException(nameof(pk));

        var vec = new List<CustomDataLink>();

        var itemsArray = items.ToArray();

        if (itemsArray.Any(o => string.IsNullOrWhiteSpace(o.name)))
            throw new ArgumentException("At least one of the items in the list doesn't have a name.", nameof(items));

        foreach (var (n, j) in itemsArray)
        {
            ct.ThrowIfCancellationRequested();
            var cid = await _ipfs.AddJsonAsync(j, pin: true, ct);

            vec.Add(new CustomDataLink
            {
                Name = n,
                Cid = cid,
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
        if (string.IsNullOrWhiteSpace(pk))
            throw new ArgumentNullException(nameof(pk));

        var vec = items.Select(i =>
        {
            if (string.IsNullOrWhiteSpace(i.name))
                throw new ArgumentNullException(nameof(i.name));
            if (string.IsNullOrWhiteSpace(i.cid))
                throw new ArgumentNullException(nameof(i.cid));

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

            /* ── rotate if full ─────────────────────────────── */
            if (_head.Links.Count >= Helpers.ChunkMaxLinks)
            {
                string closedCid = await Helpers.SaveChunk(_head, _ipfs, ct);

                foreach (var l in _head.Links)
                    _index.Entries[l.Name] = closedCid;

                _head = new NamespaceChunk { Prev = closedCid };
            }

            /* ── up‑sert … */
            int idx = _head.Links.FindIndex(l => l.Name.Equals(link.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _head.Links[idx] = link;
            }
            else
            {
                _head.Links.Add(link);
            }
        }

        /* ── flush ─────────────────────────────────────────── */
        string headCid = await Helpers.SaveChunk(_head, _ipfs, ct);
        foreach (var l in _head.Links)
        {
            _index.Entries[l.Name] = headCid;
        }

        _index.Head = headCid;

        string indexCid = await Helpers.SaveIndex(_index, _ipfs, ct);
        _ownerProfile.Namespaces[_nsKeyLower] = indexCid;
    }
}