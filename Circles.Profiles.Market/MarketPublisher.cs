using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Models.Market;
using Circles.Profiles.Sdk;

namespace Circles.Profiles.Market;

public sealed class MarketPublisher : IMarketPublisher
{
    private readonly IIpfsStore _ipfs;
    private readonly INameRegistry _registry;
    private readonly IChainApi _chain;

    public MarketPublisher(IIpfsStore ipfs, INameRegistry registry, IChainApi chain)
    {
        _ipfs = ipfs ?? throw new ArgumentNullException(nameof(ipfs));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
    }

    public async Task<string> UpsertProductAsync(
        Profile sellerProfile,
        ISigner signer,
        string operatorAddress,
        SchemaOrgProduct product,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(product.Sku))
        {
            throw new ArgumentException("Product.sku is required");
        }

        var writer = await NamespaceWriter.CreateAsync(sellerProfile, operatorAddress, _ipfs, signer, ct);

        string json = JsonSerializer.Serialize(product, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
        var link = await writer.AddJsonAsync($"product/{product.Sku}", json, ct);

        await PublishProfileDigestAsync(sellerProfile, signer, ct);
        return link.Cid;
    }

    public async Task<string> TombstoneProductAsync(
        Profile sellerProfile,
        ISigner signer,
        string operatorAddress,
        string sku,
        CancellationToken ct = default)
    {
        var tomb = new Tombstone { Sku = sku, At = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

        var writer = await NamespaceWriter.CreateAsync(sellerProfile, operatorAddress, _ipfs, signer, ct);

        string json = JsonSerializer.Serialize(tomb, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
        var link = await writer.AddJsonAsync($"product/{sku}", json, ct);

        await PublishProfileDigestAsync(sellerProfile, signer, ct);
        return link.Cid;
    }

    private async Task PublishProfileDigestAsync(Profile profile, ISigner signer, CancellationToken ct)
    {
        var store = new Circles.Profiles.Sdk.ProfileStore(_ipfs, _registry);
        await store.SaveAsync(profile, signer, ct);
    }
}
