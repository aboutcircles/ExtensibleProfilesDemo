using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

public sealed record AggregatedCatalogItem
{
    [JsonPropertyName("@type")] public string Type { get; init; } = nameof(AggregatedCatalogItem);

    [JsonPropertyName("seller")] public string Seller { get; init; } = string.Empty;
    [JsonPropertyName("productCid")] public string ProductCid { get; init; } = string.Empty;
    [JsonPropertyName("publishedAt")] public long PublishedAt { get; init; }
    [JsonPropertyName("linkKeccak")] public string LinkKeccak { get; init; } = string.Empty;

    // New: carry winner link’s index within its chunk (CPA tiebreaker)
    [JsonPropertyName("indexInChunk")] public int IndexInChunk { get; init; }

    [JsonPropertyName("product")] public required SchemaOrgProduct Product { get; init; }
}