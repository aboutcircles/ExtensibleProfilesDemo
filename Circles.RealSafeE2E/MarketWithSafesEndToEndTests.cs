using System.Text.Json;
using Circles.Profiles.Aggregation;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Market;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Market;
using Circles.Profiles.Safe;
using Circles.Profiles.Sdk;
using Circles.Profiles.Sdk.Utils;
using Microsoft.Extensions.Caching.Memory;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using JsonSerializerOptions = Circles.Profiles.Models.JsonSerializerOptions;

namespace Circles.RealSafeE2E;

[TestFixture]
public sealed class MarketWithSafesEndToEndTests
{
    private const string Rpc = "http://localhost:8545";
    private const int ChainId = 100; // Gnosis Chain (0x64)

    private Web3 _web3 = null!;
    private Account _deployer = null!;

    private string _ipfsRpcApiUrl = null!;
    private string? _ipfsRpcApiBearer;

    private EthereumChainApi _chain = null!;
    private DefaultSignatureVerifier _verifier = null!;

    private record Seller(
        string Alias,
        Account OwnerKey,
        string SafeAddr,
        Profiles.Models.Core.Profile Profile);

    private Seller _s1 = null!;
    private Seller _s2 = null!;
    private string _operatorAddr = string.Empty;

    /* ─────────────────────────────── boot env + two Safe sellers ─────────────────────────────── */
    [OneTimeSetUp]
    public async Task BootAsync()
    {
        var privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY")
                         ?? throw new ArgumentException("The PRIVATE_KEY environment variable is not set");

        _deployer = new Account(privateKey, ChainId);
        _web3 = new Web3(_deployer, Rpc);

        _ipfsRpcApiUrl = Environment.GetEnvironmentVariable("IPFS_RPC_URL")
                         ?? throw new ArgumentException("The IPFS_RPC_URL environment variable is not set");
        _ipfsRpcApiBearer = Environment.GetEnvironmentVariable("IPFS_RPC_BEARER");

        _chain = new EthereumChainApi(_web3, ChainId);
        _verifier = new DefaultSignatureVerifier(_chain);

        // Use the deployer as "operator" (market namespace key). Any address is valid as a namespace key.
        _operatorAddr = _deployer.Address.ToLowerInvariant();

        // Create two sellers, each with a 1-of-2 Safe (deployer + owner) for cheap execs in local dev
        _s1 = await CreateSellerAsync("MakerOne");
        _s2 = await CreateSellerAsync("MakerTwo");
    }

    private async Task<Seller> CreateSellerAsync(string alias)
    {
        var ownerKey = Nethereum.Signer.EthECKey.GenerateKey();
        var owner = new Account(ownerKey.GetPrivateKey(), ChainId);

        // fund owner so they can execute Safe tx
        await SafeHelper.FundAsync(_web3, _deployer, owner.Address, 0.001m);

        string safeAddr = await SafeHelper.DeploySafe141OnGnosisAsync(
            _web3, new[] { _deployer.Address, owner.Address }, threshold: 1);

        var prof = new Profiles.Models.Core.Profile
        {
            Name = alias,
            Description = "market-e2e"
        };

        return new Seller(alias, owner, safeAddr, prof);
    }

    /* ─────────────────────────────── the end-to-end market scenarios ─────────────────────────────── */
    [Test]
    public async Task Market_EndToEnd_WithSafes_AggregatesCorrectly()
    {
        await using var ipfs = new IpfsRpcApiStore(_ipfsRpcApiUrl, _ipfsRpcApiBearer);

        await PublishProductAsync(ipfs, _s1, sku: "s1-cup", name: "S1 Ceramic Cup", price: 12.50m);
        await PublishProductAsync(ipfs, _s2, sku: "s2-bag", name: "S2 Tote Bag", price: 15.00m);

        await PublishProfileDigestViaSafeAsync(ipfs, _s1);
        await PublishProfileDigestViaSafeAsync(ipfs, _s2);

        // Compose aggregator primitives directly (no wrapper)
        var mem = new MemoryCache(new MemoryCacheOptions { SizeLimit = 200 * 1024 * 1024 });
        var reg = new NameRegistry(_deployer.PrivateKey, Rpc);
        var basic = new BasicAggregator(ipfs, reg, _verifier, _verifier);
        var reducer = new CatalogReducer(ipfs);

        string op = _operatorAddr.Trim().ToLowerInvariant();
        var normalizedAvatars = new[] { _s1.SafeAddr, _s2.SafeAddr }
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var links1 = await basic.AggregateLinksAsync(op, normalizedAvatars, ChainId, 0, now);
        var (products1, errors1) = await reducer.ReduceAsync(links1.Links, links1.Errors, CancellationToken.None);
        var outcome1 = (Products: products1, Errors: errors1);

        Assert.That(outcome1.Errors.Count, Is.EqualTo(0), "No errors expected on initial publish");
        Assert.That(outcome1.Products.Count, Is.EqualTo(2), "Two products expected");

        var skus1 = outcome1.Products.Select(p => p.Product.Sku).ToHashSet();
        Assert.That(skus1, Is.SupersetOf(new[] { "s1-cup", "s2-bag" }));

        var published = outcome1.Products.Select(p => p.PublishedAt).ToArray();
        for (int i = 1; i < published.Length; i++)
        {
            Assert.That(published[i] <= published[i - 1], "Catalog must be newest-first by PublishedAt");
        }

        await PublishProductAsync(ipfs, _s1, sku: "s1-cup", name: "S1 Ceramic Cup (v2)", price: 11.00m);
        await PublishProfileDigestViaSafeAsync(ipfs, _s1);

        var prevS1 = outcome1.Products.First(p => p.Product.Sku == "s1-cup");
        var links2 = await basic.AggregateLinksAsync(op, normalizedAvatars, ChainId, 0,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var (products2, errors2) = await reducer.ReduceAsync(links2.Links, links2.Errors, CancellationToken.None);
        var outcome2 = (Products: products2, Errors: errors2);

        var s1Item = outcome2.Products.FirstOrDefault(p => p.Seller == _s1.SafeAddr.ToLowerInvariant());
        Assert.That(s1Item, Is.Not.Null, "Seller 1 must be present after update");
        Assert.That(s1Item!.PublishedAt, Is.GreaterThanOrEqualTo(prevS1.PublishedAt),
            "Updated product should not be older than previous");
        Assert.That(s1Item.Product.Name, Is.EqualTo("S1 Ceramic Cup (v2)"), "Latest product version must win");

        await TombstoneAsync(ipfs, _s2, sku: "s2-bag");
        await PublishProfileDigestViaSafeAsync(ipfs, _s2);

        var links3 = await basic.AggregateLinksAsync(_operatorAddr, new[] { _s1.SafeAddr, _s2.SafeAddr }, ChainId, 0,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var (products3, errors3) = await reducer.ReduceAsync(links3.Links, links3.Errors, CancellationToken.None);
        var outcome3 = (Products: products3, Errors: errors3);
        Assert.That(outcome3.Products.Any(p => p.Product.Sku == "s2-bag"), Is.False,
            "Tombstoned product must be excluded");
        Assert.That(outcome3.Products.Count, Is.EqualTo(1), "Only S1 product should remain");

        // ---- invalid payload path with precise error check ----
        var invalidCid = await PublishInvalidOfferAsync(ipfs, _s2, sku: "s2-bad");
        await PublishProfileDigestViaSafeAsync(ipfs, _s2);

        var links4 = await basic.AggregateLinksAsync(_operatorAddr, new[] { _s1.SafeAddr, _s2.SafeAddr }, ChainId, 0,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var (products4, errors4) = await reducer.ReduceAsync(links4.Links, links4.Errors, CancellationToken.None);
        var outcome4 = (Products: products4, Errors: errors4);

        // still no listing for the invalid item
        Assert.That(outcome4.Products.Any(p => p.Product.Sku == "s2-bad"), Is.False, "Invalid offer must be rejected");

        // confirm there is at least one error …
        Assert.That(outcome4.Errors.Count, Is.GreaterThanOrEqualTo(1), "Invalid payload must surface an error");

        // …and that one of them is exactly the payload error for the invalid CID
        bool hasMatchingPayloadError = outcome4.Errors.Any(e =>
            e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("stage", out var stage) &&
            string.Equals(stage.GetString(), "payload", StringComparison.Ordinal)
            && e.TryGetProperty("cid", out var ecid) &&
            string.Equals(ecid.GetString(), invalidCid, StringComparison.Ordinal));

        Assert.That(hasMatchingPayloadError, Is.True,
            $"Expected at least one error with stage==\"payload\" and cid=={invalidCid}");

        TestContext.Out.WriteLine(
            "[Market E2E] ✅ Aggregation scenarios passed (newest-wins, tombstone, invalid payload handling).");
    }

    /* ─────────────────────────────── helpers ─────────────────────────────── */


    private static SchemaOrgProduct BuildProduct(string sku, string name, decimal price, string checkoutUrl)
    {
        return new SchemaOrgProduct
        {
            Name = name,
            Sku = sku,
            Image = new()
            {
                new ImageRef { Url = new Uri("https://example.com/placeholder.jpg") },
                new ImageRef { Object = new SchemaOrgImageObject { ContentUrl = "ipfs://QmImage" } }
            },
            Offers = new()
            {
                new SchemaOrgOffer
                {
                    Price = price,
                    PriceCurrency = "EUR",
                    Checkout = checkoutUrl,
                    Url = "https://shop.example.com/" + sku,
                    Availability = "https://schema.org/InStock"
                }
            },
            Url = "https://shop.example.com/" + sku,
            DateCreated = DateTimeOffset.UtcNow
        };
    }

    private async Task PublishProductAsync(IIpfsStore ipfs, Seller seller, string sku, string name, decimal price)
    {
        var signer = new SafeLinkSigner(seller.SafeAddr, _chain);
        var writer = await NamespaceWriter.CreateAsync(seller.Profile, _operatorAddr, ipfs, signer);

        var product = BuildProduct(sku, name, price, checkoutUrl: $"https://buy.example.com/{sku}");
        string json = JsonSerializer.Serialize(product, JsonSerializerOptions.JsonLd);

        // The link name must be product/<sku> and the payload sku must match (enforced in aggregator)
        var link = await writer.AddJsonAsync($"product/{sku}", json, seller.OwnerKey.PrivateKey);

        TestContext.Out.WriteLine($"[publish] {seller.Alias} {sku} → CID={link.Cid}");
    }

    private async Task TombstoneAsync(IIpfsStore ipfs, Seller seller, string sku)
    {
        var signer = new SafeLinkSigner(seller.SafeAddr, _chain);
        var writer = await NamespaceWriter.CreateAsync(seller.Profile, _operatorAddr, ipfs, signer);

        var tombstone = new Tombstone { Sku = sku, At = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        string json = JsonSerializer.Serialize(tombstone, JsonSerializerOptions.JsonLd);

        var link = await writer.AddJsonAsync($"product/{sku}", json, seller.OwnerKey.PrivateKey);
        TestContext.Out.WriteLine($"[tombstone] {seller.Alias} {sku} → CID={link.Cid}");
    }

    private async Task<string> PublishInvalidOfferAsync(IIpfsStore ipfs, Seller seller, string sku)
    {
        var signer = new SafeLinkSigner(seller.SafeAddr, _chain);
        var writer = await NamespaceWriter.CreateAsync(seller.Profile, _operatorAddr, ipfs, signer);

        // Deliberately invalid: price present but no priceCurrency (violates OffersRulesOk)
        var invalid = new Dictionary<string, object?>
        {
            ["@context"] = new object[] { JsonLdMeta.SchemaOrg, JsonLdMeta.MarketContext },
            ["@type"] = "Product",
            ["name"] = "Invalid Item",
            ["sku"] = sku,
            ["image"] = new[] { "https://example.com/invalid.jpg" },
            ["offers"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "Offer",
                    ["price"] = 1.23m,
                    // intentionally omit "priceCurrency"
                    ["checkout"] = $"https://buy.example.com/{sku}"
                }
            }
        };

        string json = JsonSerializer.Serialize(invalid);
        var link = await writer.AddJsonAsync($"product/{sku}", json, seller.OwnerKey.PrivateKey);

        TestContext.Out.WriteLine($"[invalid] {seller.Alias} {sku} → CID={link.Cid}");
        return link.Cid;
    }

    private async Task PublishProfileDigestViaSafeAsync(IIpfsStore ipfs, Seller seller)
    {
        // Persist profile to IPFS first
        string profJson = JsonSerializer.Serialize(seller.Profile, JsonSerializerOptions.JsonLd);
        string cid = await ipfs.AddStringAsync(profJson, pin: true);

        byte[] digest32 = CidConverter.CidToDigest(cid);
        await SafeHelper.ExecTransactionAsync(
            _web3,
            seller.SafeAddr,
            seller.OwnerKey,
            NameRegistryConsts.ContractAddress,
            SafeHelper.EncodeUpdateDigest(digest32));

        TestContext.Out.WriteLine($"[profile] {seller.Alias} profile CID {cid}");
    }
}