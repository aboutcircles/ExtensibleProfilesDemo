using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Core;

/// <summary>
/// One tiny, append-only index per namespace.
/// • head – CID of the newest chunk
/// • entries – link-name → “owning chunk” CID
/// </summary>
public sealed record NameIndexDoc
{
    [JsonPropertyName("@context")] public string Context { get; init; } = JsonLdMeta.NamespaceContext;

    [JsonPropertyName("@type")] public string Type { get; init; } = "NameIndexDoc";

    [JsonPropertyName("head")] public string Head { get; set; } = string.Empty;

    [JsonPropertyName("entries")]
    public Dictionary<string, string> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}