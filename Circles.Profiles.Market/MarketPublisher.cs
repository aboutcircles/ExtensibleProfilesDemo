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

    // -------- existing signatures (back-compat) --------

    public async Task<string> UpsertProductAsync(
        Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        SchemaOrgProduct product,
        CancellationToken ct = default)
    {
        return await UpsertProductAsync(
            sellerProfile,
            sellerSignerPrivateKey,
            operatorAddress,
            sellerSafeAddress: null!,
            product,
            ct);
    }

    public async Task<string> TombstoneProductAsync(
        Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        string sku,
        CancellationToken ct = default)
    {
        return await TombstoneProductAsync(
            sellerProfile,
            sellerSignerPrivateKey,
            operatorAddress,
            sellerSafeAddress: null!,
            sku,
            ct);
    }

    // -------- new Safe-aware overloads (preferred) --------

    public async Task<string> UpsertProductAsync(
        Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        string sellerSafeAddress,
        SchemaOrgProduct product,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(product.Sku))
        {
            throw new ArgumentException("Product.sku is required");
        }

        ILinkSigner signer = ChooseSigner(sellerSafeAddress);
        var writer = await NamespaceWriter.CreateAsync(sellerProfile, operatorAddress, _ipfs, signer, ct);

        string json = JsonSerializer.Serialize(product, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
        var link = await writer.AddJsonAsync($"product/{product.Sku}", json, sellerSignerPrivateKey, ct);

        await PublishProfileDigestAsync(sellerProfile, sellerSignerPrivateKey, ct);
        return link.Cid;
    }

    public async Task<string> TombstoneProductAsync(
        Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        string sellerSafeAddress,
        string sku,
        CancellationToken ct = default)
    {
        var tomb = new Tombstone { Sku = sku, At = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

        ILinkSigner signer = ChooseSigner(sellerSafeAddress);
        var writer = await NamespaceWriter.CreateAsync(sellerProfile, operatorAddress, _ipfs, signer, ct);

        string json = JsonSerializer.Serialize(tomb, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
        var link = await writer.AddJsonAsync($"product/{sku}", json, sellerSignerPrivateKey, ct);

        await PublishProfileDigestAsync(sellerProfile, sellerSignerPrivateKey, ct);
        return link.Cid;
    }

    private static bool IsHexChar(char c)
    {
        bool isDigit = c >= '0' && c <= '9';
        bool isLowerHex = c >= 'a' && c <= 'f';
        bool isUpperHex = c >= 'A' && c <= 'F';
        return isDigit || isLowerHex || isUpperHex;
    }

    private ILinkSigner ChooseSigner(string? sellerSafeAddress)
    {
        bool hasSafe = !string.IsNullOrWhiteSpace(sellerSafeAddress);
        if (!hasSafe)
        {
            return new EoaLinkSigner();
        }

        string safe = sellerSafeAddress!.Trim();
        bool lengthOk = safe.Length == 42;
        bool has0x = safe.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        bool hexOk = lengthOk && has0x && safe.AsSpan(2).ToArray().All(IsHexChar);

        if (!hexOk)
        {
            throw new ArgumentException(
                "sellerSafeAddress must be a 0x-prefixed 20-byte hex address",
                nameof(sellerSafeAddress));
        }

        // normalize once so downstream never has to care about case
        string normalized = safe.ToLowerInvariant();
        return new SafeLinkSigner(normalized, _chain);
    }

    private async Task PublishProfileDigestAsync(Profile profile, string signerPrivKey, CancellationToken ct)
    {
        var store = new Circles.Profiles.Sdk.ProfileStore(_ipfs, _registry);
        await store.SaveAsync(profile, signerPrivKey, ct);
    }
}
