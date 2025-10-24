using System.Text.Json;
using Circles.Profiles.Aggregation;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Market;
using JsonSerializerOptions = Circles.Profiles.Models.JsonSerializerOptions;

namespace Circles.Profiles.Market;

public sealed class CatalogReducer
{
    private readonly IIpfsStore _ipfs;

    public CatalogReducer(IIpfsStore ipfs)
    {
        _ipfs = ipfs ?? throw new ArgumentNullException(nameof(ipfs));
    }

    public async Task<(List<AggregatedCatalogItem> products, List<JsonElement> errors)> ReduceAsync(
        List<LinkWithProvenance> orderedUnique,
        List<JsonElement> errors,
        CancellationToken ct = default)
    {
        var winners = new Dictionary<(string avatar, string sku), AggregatedCatalogItem>(capacity: orderedUnique.Count);
        var tombstoned = new HashSet<(string avatar, string sku)>();

        foreach (var it in orderedUnique)
        {
            if (!TryExtractSkuFromLinkName(it.Link.Name, out var nameSku))
            {
                continue;
            }

            string cid = it.Link.Cid;
            JsonDocument? pdoc;

            try
            {
                await using var s = await _ipfs.CatAsync(cid, ct);
                pdoc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                BasicAggregator.AddError(errors, it.Avatar, "payload", cid, ex);
                continue;
            }

            using (pdoc)
            {
                var root = pdoc.RootElement;
                var v = ClassifyPayload(root);

                if (v.Type == PayloadType.Unknown)
                {
                    continue;
                }

                if (v.Type == PayloadType.Invalid)
                {
                    BasicAggregator.AddError(errors, it.Avatar, "payload", cid, v.Error ?? "Invalid payload");
                    continue;
                }

                if (v.Type == PayloadType.Tombstone)
                {
                    if (!string.Equals(v.Sku, nameSku, StringComparison.Ordinal))
                    {
                        BasicAggregator.AddError(errors, it.Avatar, "payload", cid, "Tombstone sku does not match link name");
                        continue;
                    }

                    var key = (it.Avatar, nameSku!);
                    if (!winners.ContainsKey(key))
                    {
                        tombstoned.Add(key);
                    }
                    continue;
                }

                string sku = v.Sku!;
                if (!string.Equals(sku, nameSku, StringComparison.Ordinal))
                {
                    BasicAggregator.AddError(errors, it.Avatar, "payload", cid, "Product sku does not match link name");
                    continue;
                }

                var key2 = (it.Avatar, sku);
                if (tombstoned.Contains(key2))
                {
                    continue;
                }

                if (!winners.ContainsKey(key2))
                {
                    winners[key2] = new AggregatedCatalogItem
                    {
                        Seller = it.Avatar,
                        ProductCid = cid,
                        PublishedAt = it.Link.SignedAt,
                        LinkKeccak = it.LinkKeccak,
                        IndexInChunk = it.IndexInChunk,
                        Product = v.Product!
                    };
                }
            }
        }

        var products = winners
            .OrderBy(kv => kv.Value, new CatalogOrderComparer())
            .Select(kv => kv.Value)
            .ToList();

        return (products, errors);
    }

    /* ─── helpers copied from API reducer ───────────────────────────────── */

    private enum PayloadType { Unknown, Tombstone, Product, Invalid }

    private static (PayloadType Type, SchemaOrgProduct? Product, string? Sku, string? Error) ClassifyPayload(JsonElement root)
    {
        string type = root.TryGetProperty("@type", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;

        bool isTombstone = string.Equals(type, "Tombstone", StringComparison.Ordinal);
        if (isTombstone)
        {
            string sku = root.TryGetProperty("sku", out var p) ? (p.GetString() ?? string.Empty) : string.Empty;
            bool skuOk = LooksSku(sku);
            if (!skuOk) { return (PayloadType.Invalid, null, null, "Tombstone without valid sku"); }
            return (PayloadType.Tombstone, null, sku, null);
        }

        bool isProduct = string.Equals(type, "Product", StringComparison.Ordinal);
        if (!isProduct) { return (PayloadType.Unknown, null, null, null); }

        if (!HasRequiredProductContexts(root))
        {
            return (PayloadType.Invalid, null, null, "Product missing required @context entries");
        }

        if (!ImageShapesOk(root, out var imgErr)) { return (PayloadType.Invalid, null, null, imgErr ?? "Invalid image entry"); }
        if (!OffersRulesOk(root, out var offerErr)) { return (PayloadType.Invalid, null, null, offerErr ?? "Invalid offer(s)"); }

        if (!TryDeserializeProduct(root, out var product, out var productErr))
        {
            return (PayloadType.Invalid, null, null, $"Product deserialization failed: {productErr}");
        }

        string sku2 = product!.Sku;
        if (!LooksSku(sku2)) { return (PayloadType.Invalid, null, null, "Product missing or invalid sku"); }

        return (PayloadType.Product, product, sku2, null);
    }

    private static bool TryExtractSkuFromLinkName(string name, out string sku)
    {
        sku = string.Empty;
        if (string.IsNullOrWhiteSpace(name)) { return false; }
        if (!name.StartsWith("product/", StringComparison.OrdinalIgnoreCase)) { return false; }
        var maybe = name.Substring("product/".Length);
        if (!LooksSku(maybe)) { return false; }
        sku = maybe;
        return true;
    }

    private static bool LooksSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return false;
        if (sku.Length > 63) return false;
        char first = sku[0];
        bool firstOk = char.IsDigit(first) || (char.IsAsciiLetter(first) && char.IsLower(first));
        if (!firstOk) return false;

        for (int i = 1; i < sku.Length; i++)
        {
            char c = sku[i];
            bool ok = char.IsDigit(c) || (char.IsAsciiLetter(c) && char.IsLower(c)) || c == '-' || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    private static bool HasRequiredProductContexts(JsonElement root)
    {
        if (!root.TryGetProperty("@context", out var ctx)) { return false; }
        bool hasSchema = false, hasMarket = false;

        if (ctx.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in ctx.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var s = el.GetString();
                if (string.Equals(s, Circles.Profiles.Models.JsonLdMeta.SchemaOrg, StringComparison.Ordinal)) hasSchema = true;
                else if (string.Equals(s, Circles.Profiles.Models.JsonLdMeta.MarketContext, StringComparison.Ordinal)) hasMarket = true;
            }
        }
        else if (ctx.ValueKind == JsonValueKind.String)
        {
            var s = ctx.GetString();
            hasSchema = string.Equals(s, Circles.Profiles.Models.JsonLdMeta.SchemaOrg, StringComparison.Ordinal);
            hasMarket = string.Equals(s, Circles.Profiles.Models.JsonLdMeta.MarketContext, StringComparison.Ordinal);
        }

        return hasSchema && hasMarket;
    }

    private static bool ImageShapesOk(JsonElement root, out string? error)
    {
        error = null;
        if (!root.TryGetProperty("image", out var images)) return true;
        if (images.ValueKind != JsonValueKind.Array) return true;

        foreach (var el in images.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s) || !Uri.TryCreate(s, UriKind.Absolute, out _))
                {
                    error = "image entries must be absolute URIs."; return false;
                }
                continue;
            }

            if (el.ValueKind == JsonValueKind.Object)
            {
                bool hasContent = el.TryGetProperty("contentUrl", out var cu) && cu.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cu.GetString());
                bool hasUrl = el.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(u.GetString());
                if (!hasContent && !hasUrl) { error = "ImageObject must have contentUrl or url."; return false; }
                if (hasContent && !Uri.TryCreate(cu.GetString(), UriKind.Absolute, out _)) { error = "ImageObject.contentUrl must be an absolute URI."; return false; }
                if (hasUrl && !Uri.TryCreate(u.GetString(), UriKind.Absolute, out _)) { error = "ImageObject.url must be an absolute URI."; return false; }
                continue;
            }

            error = "image entries must be strings or objects."; return false;
        }
        return true;
    }

    private static bool OffersRulesOk(JsonElement root, out string? error)
    {
        error = null;
        if (!root.TryGetProperty("offers", out var offers)) return true;
        if (offers.ValueKind != JsonValueKind.Array) return true;

        foreach (var el in offers.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;

            bool hasPrice = el.TryGetProperty("price", out _);
            if (hasPrice)
            {
                bool hasCurrency = el.TryGetProperty("priceCurrency", out var pc) && pc.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pc.GetString());
                if (!hasCurrency) { error = "Offer has a price but is missing priceCurrency (ISO-4217)."; return false; }

                string cur = pc.GetString()!;
                if (!(cur.Length == 3 && cur.All(c => c >= 'A' && c <= 'Z')))
                {
                    error = "Offer priceCurrency should be a 3-letter ISO-4217 code (e.g., EUR, USD)."; return false;
                }
            }

            bool hasCheckout = el.TryGetProperty("checkout", out var chk) && chk.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(chk.GetString());
            if (!hasCheckout) { error = "Offer.checkout is required and must be a non-empty absolute URI."; return false; }
            if (!Uri.TryCreate(chk.GetString()!, UriKind.Absolute, out _)) { error = "Offer.checkout must be an absolute URI."; return false; }

            if (el.TryGetProperty("availabilityFeed", out var af) && af.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(af.GetString()))
            {
                if (!Uri.TryCreate(af.GetString()!, UriKind.Absolute, out _)) { error = "Offer.availabilityFeed must be an absolute URI."; return false; }
            }

            if (el.TryGetProperty("inventoryFeed", out var inf) && inf.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(inf.GetString()))
            {
                if (!Uri.TryCreate(inf.GetString()!, UriKind.Absolute, out _)) { error = "Offer.inventoryFeed must be an absolute URI."; return false; }
            }

            if (el.TryGetProperty("availability", out var av) && av.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(av.GetString()))
            {
                if (!Uri.TryCreate(av.GetString()!, UriKind.Absolute, out _)) { error = "Offer.availability must be a schema IRI (absolute URI)."; return false; }
            }
        }
        return true;
    }

    private static bool TryDeserializeProduct(JsonElement root, out SchemaOrgProduct? product, out string? error)
    {
        try
        {
            product = root.Deserialize<SchemaOrgProduct>(JsonSerializerOptions.JsonLd);
            if (product is null) { error = "null"; return false; }
            error = null; return true;
        }
        catch (Exception ex)
        {
            product = null; error = ex.Message; return false;
        }
    }
}
