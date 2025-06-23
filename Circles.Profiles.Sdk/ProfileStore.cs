// Circles.Profiles.Sdk/ProfileStore.cs

using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Sdk;
using Nethereum.Signer;

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
        if (cid is null) return null;

        using var s = await _ipfs.CatAsync(cid, ct);
        return await JsonSerializer.DeserializeAsync<Profile>(s, cancellationToken: ct);
    }

    public async Task<(Profile prof, string cid)> SaveAsync(
        Profile profile,
        string signerPrivKey,
        CancellationToken ct = default)
    {
        /* ---------- 1) pin profile JSON ---------- */
        var json = JsonSerializer.Serialize(profile);
        var cid = await _ipfs.AddJsonAsync(json, pin: true, ct);

        /* ---------- 2) registry update ---------- */
        var digest32 = CidConverter.CidToDigest(cid);

        // ← FIX: derive avatar address from the *private* key, not from the 128-byte public key
        var signerAddress = new EthECKey(signerPrivKey).GetPublicAddress();

        await _registry.UpdateProfileCidAsync(signerAddress, digest32, ct);

        return (profile, cid);
    }
}