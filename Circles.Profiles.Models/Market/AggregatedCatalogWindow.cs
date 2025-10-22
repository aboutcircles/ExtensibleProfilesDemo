using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

public sealed record AggregatedCatalogWindow
{
    [JsonPropertyName("@type")] public string Type { get; init; } = nameof(AggregatedCatalogWindow);
    [JsonPropertyName("start")] public long Start { get; init; }
    [JsonPropertyName("end")]   public long End { get; init; }
}