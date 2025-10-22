using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

/// <summary>
/// schema.org-native Product with a custom "circles-market" context for live feeds.
/// One link per product (logical name "product/<sku>").
/// </summary>
public sealed record SchemaOrgProduct
{
    [JsonPropertyName("@context")]
    public object Context { get; init; } =
        new object[] { JsonLdMeta.SchemaOrg, JsonLdMeta.MarketContext };

    [JsonPropertyName("@type")] public string Type { get; init; } = "Product";

    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>Seller-scoped stable id (formerly productId).</summary>
    [JsonPropertyName("sku")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Sku { get; init; } = string.Empty;

    [JsonPropertyName("brand")] public string? Brand { get; init; }
    [JsonPropertyName("mpn")] public string? Mpn { get; init; }
    [JsonPropertyName("gtin13")] public string? Gtin13 { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }

    /// <summary>Product images; allow either bare URLs or ImageObject.</summary>
    [JsonPropertyName("image")]
    public List<ImageRef> Image { get; init; } = new();

    /// <summary>Human-facing page.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Schema.org allows multiple offers; we typically have one.</summary>
    [JsonPropertyName("offers")]
    public List<SchemaOrgOffer> Offers { get; init; } = new();

    // Optional timestamps (ISO-8601 UTC)
    [JsonPropertyName("dateCreated")]
    [JsonConverter(typeof(Iso8601DateTimeOffsetJsonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? DateCreated { get; init; }

    [JsonPropertyName("dateModified")]
    [JsonConverter(typeof(Iso8601DateTimeOffsetJsonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? DateModified { get; init; }
}

[JsonConverter(typeof(ImageRefJsonConverter))]
public sealed record ImageRef
{
    public Uri? Url { get; init; }
    public SchemaOrgImageObject? Object { get; init; }
}