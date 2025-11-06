using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Circles.Profiles.Models;

namespace Circles.Market.Api;

public interface IJsonLdShapeVerifier
{
    bool CanPin(ReadOnlyMemory<byte> jsonUtf8, out string? reason);
}

internal sealed class JsonLdShapeVerifier : IJsonLdShapeVerifier
{
    // CIDv0 (sha2-256, base58btc, "Qm" + 44 chars)
    private static readonly Regex CidV0 =
        new("^Qm[1-9A-HJ-NP-Za-km-z]{44}$", RegexOptions.Compiled);

    private static readonly Regex HexLower = new("^0x[0-9a-f]+$", RegexOptions.Compiled);
    private static readonly Regex HexAny = new("^0x[0-9a-fA-F]+$", RegexOptions.Compiled);
    private static readonly Regex Eip55AddrShape = new("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    private static readonly Regex Iso4217Upper = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public bool CanPin(ReadOnlyMemory<byte> jsonUtf8, out string? reason)
    {
        reason = null;

        try
        {
            using var doc = JsonDocument.Parse(jsonUtf8);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = "Root must be a JSON object";
                return false;
            }

            // --- @type
            if (!root.TryGetProperty("@type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
            {
                reason = "Missing or invalid @type";
                return false;
            }

            string type = typeProp.GetString() ?? string.Empty;

            // --- @context (collect string entries only; arrays may include a mapping object which we ignore)
            if (!root.TryGetProperty("@context", out var ctxProp))
            {
                reason = "Missing @context";
                return false;
            }

            var ctx = new HashSet<string>(StringComparer.Ordinal);
            switch (ctxProp.ValueKind)
            {
                case JsonValueKind.String:
                    AddIfNonEmpty(ctxProp.GetString());
                    break;

                case JsonValueKind.Array:
                    foreach (var el in ctxProp.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            AddIfNonEmpty(el.GetString());
                        }
                        else if (el.ValueKind == JsonValueKind.Object)
                        {
                            // Allowed: context array often contains a mapping object; ignore for allow-listing.
                        }
                        else
                        {
                            reason = "Invalid @context";
                            return false;
                        }
                    }

                    break;

                case JsonValueKind.Object:
                    // We don’t reject object context per se, but without a schema URL we can’t whitelist safely.
                    reason = "Top-level @context object is not supported; include the schema URL string(s) as well.";
                    return false;

                default:
                    reason = "Invalid @context";
                    return false;
            }

            // Hard exclusion: market aggregate documents must never be pinned here.
            if (ctx.Contains(JsonLdMeta.MarketAggregateContext))
            {
                reason = "Market aggregate documents are generated and cannot be pinned via this endpoint.";
                return false;
            }

            // Accept any valid model from our contexts (except aggregate), with per-type validation.
            // Keep the checks deliberately strict but cheap: we validate shape, not semantics that require RPC.
            if (type.Equals("Profile", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.ProfileContext))
            {
                // Minimal shape is fine; fields inside are validated elsewhere when used.
                return true;
            }

            if (type.Equals("SigningKey", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.ProfileContext))
            {
                return ValidateSigningKey(root, out reason);
            }

            if (type.Equals("CustomDataLink", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.LinkContext))
            {
                return ValidateCustomDataLink(root, out reason);
            }

            if (type.Equals("BasicMessage", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.ChatContext))
            {
                return ValidateBasicMessage(root, out reason);
            }

            // Market models
            if (type.Equals("Product", StringComparison.Ordinal) &&
                ctx.Contains(JsonLdMeta.SchemaOrg) &&
                ctx.Contains(JsonLdMeta.MarketContext))
            {
                return ValidateProduct(root, out reason);
            }

            if (type.Equals("Tombstone", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.MarketContext))
            {
                return ValidateTombstone(root, out reason);
            }

            // Allow top-level ImageObject & Offer if someone wants to pin those directly (they're valid models too).
            if (type.Equals("ImageObject", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.SchemaOrg))
            {
                return ValidateImageObject(root, out reason);
            }

            if (type.Equals("Offer", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.SchemaOrg))
            {
                return ValidateOffer(root, out reason);
            }

            // Namespace primitives (yes, allow them; the IPFS node already enforces 8 MiB cap).
            if (type.Equals("NameIndexDoc", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.NamespaceContext))
            {
                return ValidateNameIndexDoc(root, out reason);
            }

            if (type.Equals("NamespaceChunk", StringComparison.Ordinal) && ctx.Contains(JsonLdMeta.NamespaceContext))
            {
                return ValidateNamespaceChunk(root, out reason);
            }

            reason = $"Unsupported JSON-LD shape: @type='{type}', @context='[" + string.Join(",", ctx) + "]'";
            return false;

            void AddIfNonEmpty(string? s)
            {
                if (!string.IsNullOrWhiteSpace(s)) ctx.Add(s!);
            }
        }
        catch (JsonException ex)
        {
            reason = $"Malformed JSON: {ex.Message}";
            return false;
        }
    }

    /* ────────────────────────── validators ────────────────────────── */

    private static bool ValidateSigningKey(JsonElement root, out string? reason)
    {
        reason = null;

        if (!TryGetString(root, "publicKey", out var pk) || string.IsNullOrWhiteSpace(pk))
        {
            reason = "SigningKey.publicKey is required";
            return false;
        }

        if (!TryGetLong(root, "validFrom", out _))
        {
            reason = "SigningKey.validFrom is required and must be a unix timestamp";
            return false;
        }

        if (root.TryGetProperty("validTo", out var _))
        {
            if (!TryGetNullableLong(root, "validTo", out _))
            {
                reason = "SigningKey.validTo must be a unix timestamp when present";
                return false;
            }
        }

        if (root.TryGetProperty("revokedAt", out var _))
        {
            if (!TryGetNullableLong(root, "revokedAt", out _))
            {
                reason = "SigningKey.revokedAt must be a unix timestamp when present";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateCustomDataLink(JsonElement root, out string? reason)
    {
        reason = null;

        if (!TryGetString(root, "name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            reason = "CustomDataLink.name is required";
            return false;
        }

        if (!TryGetString(root, "cid", out var cid) || !CidV0.IsMatch(cid))
        {
            reason = "CustomDataLink.cid must be a CIDv0 (base58btc “Qm…”)";
            return false;
        }

        if (!TryGetLong(root, "chainId", out _))
        {
            reason = "CustomDataLink.chainId is required";
            return false;
        }

        if (!TryGetString(root, "signerAddress", out var addr) || !Eip55AddrShape.IsMatch(addr))
        {
            reason = "CustomDataLink.signerAddress must be a 0x-prefixed 20-byte hex address";
            return false;
        }

        if (!TryGetLong(root, "signedAt", out _))
        {
            reason = "CustomDataLink.signedAt is required and must be a unix timestamp";
            return false;
        }

        if (!TryGetString(root, "nonce", out var nonce) || !HexAny.IsMatch(nonce))
        {
            reason = "CustomDataLink.nonce must be a 0x-hex string";
            return false;
        }

        if (!TryGetString(root, "signature", out var sig) || !HexAny.IsMatch(sig))
        {
            reason = "CustomDataLink.signature must be a 0x-hex string";
            return false;
        }

        // encrypted is required (bool) even if false
        if (!root.TryGetProperty("encrypted", out var encProp) ||
            encProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            reason = "CustomDataLink.encrypted must be present (boolean)";
            return false;
        }

        return true;
    }

    private static bool ValidateBasicMessage(JsonElement root, out string? reason)
    {
        reason = null;

        if (!TryGetString(root, "from", out var from) || !Eip55AddrShape.IsMatch(from))
        {
            reason = "BasicMessage.from must be a 0x-prefixed 20-byte hex address";
            return false;
        }

        if (!TryGetString(root, "to", out var to) || !Eip55AddrShape.IsMatch(to))
        {
            reason = "BasicMessage.to must be a 0x-prefixed 20-byte hex address";
            return false;
        }

        if (!TryGetString(root, "text", out var txt) || string.IsNullOrWhiteSpace(txt))
        {
            reason = "BasicMessage.text is required";
            return false;
        }

        if (!TryGetLong(root, "ts", out _))
        {
            reason = "BasicMessage.ts is required and must be a unix timestamp";
            return false;
        }

        return true;
    }

    private static bool ValidateProduct(JsonElement root, out string? reason)
    {
        reason = null;

        if (!TryGetString(root, "name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            reason = "Product.name is required";
            return false;
        }

        if (!root.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Array ||
            offers.GetArrayLength() == 0)
        {
            reason = "Product.offers must be a non-empty array";
            return false;
        }

        // Validate at least the first offer strictly; others must not be obviously invalid.
        int i = 0;
        foreach (var offer in offers.EnumerateArray())
        {
            if (!ValidateOffer(offer, out var offerErr))
            {
                reason = $"Offer[{i}]: {offerErr}";
                return false;
            }

            i++;
        }

        // Optional images
        if (root.TryGetProperty("image", out var imgProp))
        {
            if (imgProp.ValueKind != JsonValueKind.Array)
            {
                reason = "Product.image must be an array when present";
                return false;
            }

            foreach (var img in imgProp.EnumerateArray())
            {
                if (img.ValueKind == JsonValueKind.String)
                {
                    var s = img.GetString();
                    if (!IsAbsoluteUri(s))
                    {
                        reason = "Product.image[] string must be an absolute URI";
                        return false;
                    }
                }
                else if (img.ValueKind == JsonValueKind.Object)
                {
                    if (!ValidateImageObject(img, out var imgErr))
                    {
                        reason = $"Product.image[]: {imgErr}";
                        return false;
                    }
                }
                else
                {
                    reason = "Product.image[] must be string or ImageObject";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateTombstone(JsonElement root, out string? reason)
    {
        reason = null;

        if (!TryGetString(root, "sku", out var sku) || string.IsNullOrWhiteSpace(sku))
        {
            reason = "Tombstone.sku is required";
            return false;
        }

        return true;
    }

    private static bool ValidateImageObject(JsonElement obj, out string? reason)
    {
        reason = null;

        // contentUrl (ipfs://, ar://, data:, http(s)://) OR url (http(s)://)
        bool hasContent = obj.TryGetProperty("contentUrl", out var cu) && cu.ValueKind == JsonValueKind.String;
        bool hasUrl = obj.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String;

        if (!hasContent && !hasUrl)
        {
            reason = "ImageObject needs contentUrl or url";
            return false;
        }

        if (hasContent && !IsImageTransport(cu.GetString()))
        {
            reason = "ImageObject.contentUrl must be ipfs://, cid://, ar://, data: or absolute http(s)://";
            return false;
        }

        if (hasUrl && !IsHttpUrl(u.GetString()))
        {
            reason = "ImageObject.url must be absolute http(s)://";
            return false;
        }

        return true;
    }

    private static bool ValidateOffer(JsonElement offer, out string? reason)
    {
        reason = null;

        if (!TryGetDecimal(offer, "price", out _))
        {
            reason = "Offer.price is required";
            return false;
        }

        if (!TryGetString(offer, "priceCurrency", out var cur) || !Iso4217Upper.IsMatch(cur))
        {
            reason = "Offer.priceCurrency must be an ISO-4217 three-letter uppercase code";
            return false;
        }

        if (!TryGetString(offer, "checkout", out var checkout) || !IsAbsoluteUri(checkout))
        {
            reason = "Offer.checkout must be an absolute URI";
            return false;
        }

        if (offer.TryGetProperty("seller", out var seller) && seller.ValueKind == JsonValueKind.Object)
        {
            if (!seller.TryGetProperty("@id", out var idProp) || idProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                reason = "Offer.seller.@id must be present when seller is provided";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateNameIndexDoc(JsonElement root, out string? reason)
    {
        reason = null;

        if (!TryGetString(root, "head", out var head) || string.IsNullOrWhiteSpace(head))
        {
            reason = "NameIndexDoc.head is required";
            return false;
        }

        // We don’t force CID shape here (can be a preview hash), but if it looks like CIDv0, enforce strictly.
        if (head.StartsWith("Qm", StringComparison.Ordinal) && !CidV0.IsMatch(head))
        {
            reason = "NameIndexDoc.head must be a valid CIDv0 when using base58btc form";
            return false;
        }

        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Object)
        {
            reason = "NameIndexDoc.entries must be an object";
            return false;
        }

        foreach (var kv in entries.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(kv.Name))
            {
                reason = "NameIndexDoc.entries has an empty key";
                return false;
            }

            if (kv.Value.ValueKind != JsonValueKind.String)
            {
                reason = "NameIndexDoc.entries values must be strings (chunk CIDs)";
                return false;
            }

            var v = kv.Value.GetString();
            if (v != null && v.StartsWith("Qm", StringComparison.Ordinal) && !CidV0.IsMatch(v))
            {
                reason = $"NameIndexDoc.entries['{kv.Name}'] is not a valid CIDv0";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateNamespaceChunk(JsonElement root, out string? reason)
    {
        reason = null;

        // prev: nullable string
        if (root.TryGetProperty("prev", out var prev) &&
            prev.ValueKind is not JsonValueKind.Null and not JsonValueKind.String)
        {
            reason = "NamespaceChunk.prev must be a string or null";
            return false;
        }

        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            reason = "NamespaceChunk.links must be an array";
            return false;
        }

        // Quick sanity on each link (don’t fully re-validate here)
        foreach (var el in links.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                reason = "NamespaceChunk.links[] must be objects";
                return false;
            }

            if (!el.TryGetProperty("name", out var _) ||
                !el.TryGetProperty("cid", out var _) ||
                !el.TryGetProperty("signerAddress", out var _) ||
                !el.TryGetProperty("signature", out var _))
            {
                reason = "NamespaceChunk.links[] must at least contain name, cid, signerAddress, signature";
                return false;
            }
        }

        return true;
    }

    /* ────────────────────────── utils ────────────────────────── */

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return false;
        value = p.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetLong(JsonElement obj, string name, out long value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind is JsonValueKind.Number && p.TryGetInt64(out var i))
        {
            value = i;
            return true;
        }

        if (p.ValueKind is JsonValueKind.String && long.TryParse(p.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var s))
        {
            value = s;
            return true;
        }

        return false;
    }

    private static bool TryGetNullableLong(JsonElement obj, string name, out long? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var p)) return true; // absent is ok
        if (p.ValueKind == JsonValueKind.Null) return true;
        if (p.ValueKind is JsonValueKind.Number && p.TryGetInt64(out var i))
        {
            value = i;
            return true;
        }

        if (p.ValueKind is JsonValueKind.String && long.TryParse(p.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var s))
        {
            value = s;
            return true;
        }

        return false;
    }

    private static bool TryGetDecimal(JsonElement obj, string name, out decimal value)
    {
        value = 0m;
        if (!obj.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d))
        {
            value = d;
            return true;
        }

        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var s))
        {
            value = s;
            return true;
        }

        return false;
    }

    private static bool IsAbsoluteUri(string? s)
        => s is not null && Uri.TryCreate(s, UriKind.Absolute, out _);

    private static bool IsHttpUrl(string? s)
        => Uri.TryCreate(s, UriKind.Absolute, out var u) &&
           (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static bool IsImageTransport(string? s)
    {
        if (s is null) return false;
        if (IsHttpUrl(s)) return true;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) &&
               (u.Scheme.Equals("ipfs", StringComparison.OrdinalIgnoreCase) ||
                u.Scheme.Equals("cid", StringComparison.OrdinalIgnoreCase) ||
                u.Scheme.Equals("ar", StringComparison.OrdinalIgnoreCase) ||
                u.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase));
    }
}