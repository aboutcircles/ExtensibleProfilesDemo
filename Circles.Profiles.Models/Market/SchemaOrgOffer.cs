using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

/// <summary>Minimal schema.org Offer + circles-market feed pointers.</summary>
public sealed record SchemaOrgOffer
{
    [JsonPropertyName("@type")] public string Type { get; init; } = "Offer";

    // Pricing (schema.org requires priceCurrency when price is present)
    [JsonPropertyName("price")] public decimal Price { get; init; }
    [JsonPropertyName("priceCurrency")] public required string PriceCurrency { get; init; } // ISO-4217

    // Stock / availability (schema IRIs)
    [JsonPropertyName("availability")] public string? Availability { get; init; }

    /// <summary>Optional live availability feed that can be GET by the client</summary>
    [JsonPropertyName("availabilityFeed")]
    public string? CirclesAvailabilityFeed { get; init; }

    /// <summary>Explicit finite stock (optional).</summary>
    [JsonPropertyName("inventoryLevel")]
    public SchemaOrgQuantitativeValue? InventoryLevel { get; init; }

    /// <summary>Optional live inventory feed that can be GET by the client</summary>
    [JsonPropertyName("inventoryFeed")]
    public string? CirclesInventoryFeed { get; init; }

    /// <summary>Required: a url that points to the checkout flow for this offer. Usually a circles payment link.</summary>
    [JsonPropertyName("checkout")]
    public required string Checkout { get; init; }

    /// <summary>Optional: POST endpoint to signal a buy intent and receive a token.</summary>
    [JsonPropertyName("signalBuyIntent")]
    public string? SignalBuyIntent { get; init; }

    /// <summary>Offer page.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Seller identity; use @id like "eip155:100:0x…". Usually empty because the seller can already be identified as the CustomDataLink signer.</summary>
    [JsonPropertyName("seller")]
    public SchemaOrgSeller? Seller { get; init; } = null;

    // Optional validity/modified time (ISO-8601 UTC)
    [JsonPropertyName("priceValidUntil")]
    [JsonConverter(typeof(Iso8601DateTimeOffsetJsonConverter))]
    public DateTimeOffset? PriceValidUntil { get; init; }

    [JsonPropertyName("dateModified")]
    [JsonConverter(typeof(Iso8601DateTimeOffsetJsonConverter))]
    public DateTimeOffset? DateModified { get; init; }
}