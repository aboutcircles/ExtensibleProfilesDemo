using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

public sealed record SchemaOrgQuantitativeValue
{
    [JsonPropertyName("@type")] public string Type { get; init; } = "QuantitativeValue";
    [JsonPropertyName("value")] public long Value { get; init; }

    // Optional; recommended for interop with some commerce stacks (C62 = count)
    [JsonPropertyName("unitCode")] public string? UnitCode { get; init; }
}