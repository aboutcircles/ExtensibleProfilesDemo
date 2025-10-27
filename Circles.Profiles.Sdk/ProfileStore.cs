using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Utils;

namespace Circles.Profiles.Sdk;

/// <summary>Profile CRUD – *no* business logic beyond pin+publish.</summary>
public sealed class ProfileStore : IProfileStore
{
    private readonly IIpfsStore _ipfs;
    private readonly INameRegistry _registry;

    public ProfileStore(IIpfsStore ipfs, INameRegistry registry)
    {
        _ipfs = ipfs;
        _registry = registry;
    }

    public async Task<Profile?> FindAsync(string avatar, CancellationToken ct = default)
    {
        var cid = await _registry.GetProfileCidAsync(avatar, ct);
        if (cid is null)
        {
            return null;
        }

        await using var s = await _ipfs.CatAsync(cid, ct);
        return await JsonSerializer.DeserializeAsync<Profile>(s, Models.JsonSerializerOptions.JsonLd, ct);
    }

    public async Task<(Profile prof, string cid)> SaveAsync(
        Profile profile,
        ISigner signer,
        CancellationToken ct = default)
    {
        if (signer is null) { throw new ArgumentNullException(nameof(signer)); }

        /* ---------- 1) pin profile JSON ---------- */
        var json = JsonSerializer.Serialize(profile, Models.JsonSerializerOptions.JsonLd);
        var cid = await _ipfs.AddStringAsync(json, pin: true, ct);

        /* ---------- 2) registry update ---------- */
        var digest32 = CidConverter.CidToDigest(cid);

        await _registry.UpdateProfileCidAsync(signer.Address, digest32, ct);

        return (profile, cid);
    }
}