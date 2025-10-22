using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

public sealed record SchemaOrgSeller
{
    [JsonPropertyName("@type")] public string Type { get; init; } = "Organization";
    [JsonPropertyName("@id")] public string Id { get; init; } = string.Empty; // eip155:chainId:address
    [JsonPropertyName("name")] public string? Name { get; init; }
}