// uses the same chunk & index logic you had, but refactored

using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;

namespace Circles.Profiles.Sdk;

public sealed class NamespaceWriter : INamespaceWriter
{
    private readonly Profile _ownerProfile;
    private readonly string _nsKeyLower; // recipient-addr lower
    private readonly IIpfsStore _ipfs;
    private readonly ILinkSigner _signer;

    private NameIndexDoc _index;
    private NamespaceChunk _head; // lazily loaded

    public NamespaceWriter(Profile ownerProfile,
        string namespaceKey, // e.g. recipient address
        IIpfsStore ipfs,
        ILinkSigner signer)
    {
        _ownerProfile = ownerProfile;
        _nsKeyLower = namespaceKey.ToLowerInvariant();
        _ipfs = ipfs;
        _signer = signer;
        _index = ownerProfile.Namespaces.TryGetValue(_nsKeyLower, out var cid)
            ? Helpers.LoadIndex(cid, ipfs).GetAwaiter().GetResult()
            : new NameIndexDoc();
        _head = Helpers.LoadChunk(_index.Head, ipfs).GetAwaiter().GetResult();
    }

    /* single ------------------------------------------------------ */

    public async Task<CustomDataLink> AddJsonAsync(string name, string json,
        string pk, CancellationToken ct = default)
    {
        var cid = await _ipfs.AddJsonAsync(json, pin: true, ct);
        return await AttachExistingCidAsync(name, cid, pk, ct);
    }

    public async Task<CustomDataLink> AttachExistingCidAsync(string name, string cid,
        string pk, CancellationToken ct = default)
    {
        var link = _signer.Sign(new CustomDataLink
        {
            Name = name,
            Cid = cid,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = CustomDataLink.NewNonce(),
            Encrypted = false
        }, pk);

        await PersistAsync([link], ct);
        return link;
    }

    /* bulk -------------------------------------------------------- */

    public async Task<IReadOnlyList<CustomDataLink>> AddJsonBatchAsync(
        IEnumerable<(string name, string json)> items, string pk, CancellationToken ct = default)
    {
        var vec = new List<CustomDataLink>();
        foreach (var (n, j) in items)
        {
            var cid = await _ipfs.AddJsonAsync(j, pin: true, ct);
            vec.Add(new CustomDataLink { Name = n, Cid = cid, /* ... */ });
        }

        vec = vec.Select(l => _signer.Sign(l, pk)).ToList();
        await PersistAsync(vec, ct);
        return vec;
    }

    public async Task<IReadOnlyList<CustomDataLink>> AttachCidBatchAsync(
        IEnumerable<(string name, string cid)> items, string pk, CancellationToken ct = default)
    {
        var vec = items.Select(i => new CustomDataLink
        {
            Name = i.name,
            Cid = i.cid,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = CustomDataLink.NewNonce()
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
            /* ── rotate if full ─────────────────────────────── */
            if (_head.Links.Count >= Helpers.ChunkMaxLinks)
            {
                string closedCid = await Helpers.SaveChunk(_head, _ipfs, ct);

                // back-patch every name that lives in this (now closed) chunk
                foreach (var l in _head.Links)
                    _index.Entries[l.Name] = closedCid;

                _head = new NamespaceChunk { Prev = closedCid };
            }

            /* ── up-sert in the *current* chunk ─────────────── */
            int idx = _head.Links.FindIndex(l =>
                l.Name.Equals(link.Name, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
                _head.Links[idx] = link; // replace
            else
                _head.Links.Add(link);
        }

        /* ── flush the open chunk ───────────────────────────── */
        string headCid = await Helpers.SaveChunk(_head, _ipfs, ct);

        foreach (var l in _head.Links)
            _index.Entries[l.Name] = headCid; // now we know the CID

        _index.Head = headCid;

        string indexCid = await Helpers.SaveIndex(_index, _ipfs, ct);
        _ownerProfile.Namespaces[_nsKeyLower] = indexCid;
    }
}