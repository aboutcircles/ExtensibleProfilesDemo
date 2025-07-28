using System.Text.Json.Serialization;

namespace Circles.Profiles.Models;

public sealed record Profile
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.1";

    [JsonPropertyName("previewImageUrl")]
    public string? PreviewImageUrl { get; init; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>namespace → *head‑of‑index* CID</summary>
    [JsonPropertyName("namespaces")]
    public Dictionary<string, string> Namespaces { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("signingKeys")]
    public Dictionary<string, SigningKey> SigningKeys { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}