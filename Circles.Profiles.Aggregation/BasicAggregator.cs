using System.Collections.Concurrent;
using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk;
using Circles.Profiles.Sdk.Utils;
using Nethereum.Hex.HexConvertors.Extensions;
using JsonSerializerOptions = Circles.Profiles.Models.JsonSerializerOptions;

namespace Circles.Profiles.Aggregation;

/// <summary>
/// Basic operator-centric aggregator:
/// - resolves namespace heads for (avatar, operator)
/// - streams namespace chunks newest-first
/// - applies chainId and time window filter
/// - verifies signatures (EOA and optional ERC-1271 Safe bytes)
/// - returns ordered, de-duplicated links with provenance
///
/// This aggregator is payload-agnostic and works with all links.
/// </summary>
public sealed class BasicAggregator
{
    public async Task<AggregationLinksOutcome> AggregateLinksAsync(
        string op,
        IReadOnlyList<string> avatars,
        long chainId,
        long windowStart,
        long windowEnd,
        CancellationToken ct = default)
    {
        var errors = new List<JsonElement>();
        var avatarsScanned = avatars.ToList();

        var indexHeads = await ResolveIndexHeadsAsync(op, avatars, errors, ct);

        var verified = await StreamVerifiedLinksAsync(
            op, indexHeads, chainId, windowStart, windowEnd, errors, ct);

        var orderedUnique = OrderAndDeduplicate(verified);

        return new AggregationLinksOutcome(avatarsScanned, indexHeads, orderedUnique, errors);
    }

    private readonly IIpfsStore _ipfs;
    private readonly INameRegistry _registry;
    private readonly ISignatureVerifier _verifier;
    private readonly ISafeBytesVerifier? _safeBytesVerifier;

    public BasicAggregator(
        IIpfsStore ipfs,
        INameRegistry registry,
        ISignatureVerifier verifier,
        ISafeBytesVerifier? safeBytesVerifier = null)
    {
        _ipfs = ipfs;
        _registry = registry;
        _verifier = verifier;
        _safeBytesVerifier = safeBytesVerifier;
    }

    public async Task<Dictionary<string, string>> ResolveIndexHeadsAsync(
        string op,
        IReadOnlyList<string> avatars,
        List<JsonElement> errors,
        CancellationToken ct)
    {
        var indexHeads = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string avatar in avatars)
        {
            string? profileCid;
            try
            {
                profileCid = await _registry.GetProfileCidAsync(avatar, ct);
            }
            catch (Exception ex)
            {
                AddError(errors, avatar, ErrorStage.Registry, null, ex);
                continue;
            }

            bool noProfile = string.IsNullOrWhiteSpace(profileCid);
            if (noProfile)
            {
                AddError(errors, avatar, ErrorStage.Registry, null, "Profile not found in registry (no CID)");
                continue;
            }

            Profile profile;
            try
            {
                await using var s = await _ipfs.CatAsync(profileCid!, ct);
                profile = await JsonSerializer.DeserializeAsync<Profile>(s, JsonSerializerOptions.JsonLd, ct)
                          ?? new Profile();
            }
            catch (Exception ex)
            {
                AddError(errors, avatar, ErrorStage.Profile, null, ex);
                continue;
            }

            string nsKey = op;
            bool hasIndex = profile.Namespaces.TryGetValue(nsKey, out var indexCid) &&
                            !string.IsNullOrWhiteSpace(indexCid);
            if (!hasIndex)
            {
                AddError(errors, avatar, ErrorStage.Index, null, $"Namespace for operator {op} not found in profile");
                continue;
            }

            try
            {
                var index = await Helpers.LoadIndex(indexCid!, _ipfs, ct);
                bool headEmpty = string.IsNullOrWhiteSpace(index.Head);
                if (headEmpty)
                {
                    AddError(errors, avatar, ErrorStage.Index, indexCid, "Index head is empty");
                    continue;
                }

                indexHeads[avatar] = index.Head;
            }
            catch (Exception ex)
            {
                AddError(errors, avatar, ErrorStage.Index, indexCid, ex);
            }
        }

        return indexHeads;
    }

    public async Task<List<LinkWithProvenance>> StreamVerifiedLinksAsync(
        string op,
        IReadOnlyDictionary<string, string> indexHeads,
        long chainId,
        long windowStart,
        long windowEnd,
        List<JsonElement> errors,
        CancellationToken ct)
    {
        var items = new List<LinkWithProvenance>(capacity: 512);
        var nonceScopes = new ConcurrentDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var window = new TimeWindow(windowStart, windowEnd);

        foreach (var (avatar, head) in indexHeads)
        {
            string? cur = head;

            while (!string.IsNullOrWhiteSpace(cur))
            {
                NamespaceChunk chunk;
                try
                {
                    chunk = await LoadChunk(cur, ct);
                }
                catch (Exception ex)
                {
                    AddError(errors, avatar, ErrorStage.Chunk, cur, ex);
                    break;
                }

                for (int i = 0; i < chunk.Links.Count; i++)
                {
                    var link = chunk.Links[i];

                    bool chainMatches = link.ChainId == chainId;
                    if (!chainMatches)
                    {
                        continue;
                    }

                    bool inWindow = window.Contains(link.SignedAt);
                    if (!inWindow)
                    {
                        continue;
                    }

                    byte[] canonical = CanonicalJson.CanonicaliseWithoutSignature(link);
                    byte[] keccak = Sha3.Keccak256Bytes(canonical);
                    string linkKeccakHex = "0x" + keccak.ToHex();

                    string signer = link.SignerAddress.ToLowerInvariant();
                    string nonce = link.Nonce.ToLowerInvariant();

                    string scopeKey = $"{avatar}|{op}|{signer}";
                    var scope = nonceScopes.GetOrAdd(scopeKey, _ => new HashSet<string>(StringComparer.Ordinal));

                    bool seenBefore = scope.Contains(nonce);
                    if (seenBefore)
                    {
                        continue;
                    }

                    bool ok;
                    try
                    {
                        byte[] sig = link.Signature.HexToByteArray();
                        ok = await VerifyLinkSignatureAsync(keccak, canonical, signer, sig, ct);
                        if (!ok)
                        {
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddError(errors, avatar, ErrorStage.Verify, cur, ex);
                        continue;
                    }

                    scope.Add(nonce);
                    items.Add(new LinkWithProvenance(
                        Avatar: avatar,
                        ChunkCid: cur,
                        IndexInChunk: i,
                        Link: link,
                        LinkKeccak: linkKeccakHex));
                }

                cur = chunk.Prev;
            }
        }

        return items;
    }

    public static List<LinkWithProvenance> OrderAndDeduplicate(List<LinkWithProvenance> items)
    {
        items.Sort((a, b) =>
        {
            int byTs = b.Link.SignedAt.CompareTo(a.Link.SignedAt);
            if (byTs != 0)
            {
                return byTs;
            }

            int byIdx = b.IndexInChunk.CompareTo(a.IndexInChunk);
            if (byIdx != 0)
            {
                return byIdx;
            }

            int byAvatar = string.CompareOrdinal(a.Avatar, b.Avatar);
            if (byAvatar != 0)
            {
                return byAvatar;
            }

            return string.CompareOrdinal(a.LinkKeccak, b.LinkKeccak);
        });

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<LinkWithProvenance>(items.Count);

        foreach (var it in items)
        {
            bool firstTime = seen.Add(it.LinkKeccak);
            if (firstTime)
            {
                unique.Add(it);
            }
        }

        return unique;
    }

    private async Task<NamespaceChunk> LoadChunk(string cid, CancellationToken ct)
    {
        await using var s = await _ipfs.CatAsync(cid, ct);
        var chunk = await JsonSerializer.DeserializeAsync<NamespaceChunk>(s, JsonSerializerOptions.JsonLd, ct);
        if (chunk is null)
        {
            throw new JsonException("Empty NamespaceChunk");
        }

        return chunk;
    }

    private async Task<bool> VerifyLinkSignatureAsync(
        byte[] linkKeccak,
        byte[] canonicalBytes,
        string signer,
        byte[] signature,
        CancellationToken ct)
    {
        bool ok = await _verifier.VerifyAsync(linkKeccak, signer, signature, ct);
        if (!ok && _safeBytesVerifier is not null)
        {
            // ERC-1271 Safe bytes path
            ok = await _safeBytesVerifier.Verify1271WithBytesAsync(canonicalBytes, signer, signature, ct);
        }

        return ok;
    }

    private static JsonElement ToError(string? avatar, string stage, string? cid, string message)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            avatar = string.IsNullOrWhiteSpace(avatar) ? null : avatar,
            stage,
            cid = string.IsNullOrWhiteSpace(cid) ? null : cid,
            message
        }));
        return doc.RootElement.Clone();
    }

    private static class ErrorStage
    {
        public const string Registry = "registry";
        public const string Profile = "profile";
        public const string Index = "index";
        public const string Chunk = "chunk";
        public const string Verify = "verify";
    }

    public static void AddError(List<JsonElement> errors, string? avatar, string stage, string? cid, string message)
    {
        errors.Add(ToError(avatar, stage, cid, message));
    }

    public static void AddError(List<JsonElement> errors, string? avatar, string stage, string? cid, Exception ex)
    {
        string message = ex is JsonException ? $"Malformed JSON: {ex.Message}" : ex.Message;
        errors.Add(ToError(avatar, stage, cid, message));
    }
}