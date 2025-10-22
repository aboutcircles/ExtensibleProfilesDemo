using System.Text.Json;
using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

/// <summary>Aggregation output for operator across many sellers (§7.2).</summary>
public sealed record AggregatedCatalog
{
    [JsonPropertyName("@context")] public string Context { get; init; } = JsonLdMeta.MarketAggregateContext;
    [JsonPropertyName("@type")] public string Type { get; init; } = nameof(AggregatedCatalog);

    [JsonPropertyName("operator")] public string Operator { get; init; } = string.Empty;
    [JsonPropertyName("chainId")] public long ChainId { get; init; }
    [JsonPropertyName("window")] public AggregatedCatalogWindow Window { get; init; } = new();
    [JsonPropertyName("avatarsScanned")] public List<string> AvatarsScanned { get; init; } = new();
    [JsonPropertyName("products")] public List<AggregatedCatalogItem> Products { get; init; } = new();
    [JsonPropertyName("errors")] public List<JsonElement> Errors { get; init; } = new();
}