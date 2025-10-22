using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

/// <summary>Tombstone payload to logically delete a product (§5).</summary>
public sealed record Tombstone
{
    [JsonPropertyName("@context")] public string Context { get; init; } = JsonLdMeta.MarketContext;
    [JsonPropertyName("@type")] public string Type { get; init; } = nameof(Tombstone);

    // align with schema.org product identity
    [JsonPropertyName("sku")] public string Sku { get; init; } = string.Empty;

    // Unix seconds when tombstoned
    [JsonPropertyName("at")] public long At { get; init; }
}