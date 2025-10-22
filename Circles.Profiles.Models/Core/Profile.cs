using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Core;

public sealed record Profile
{
    [JsonPropertyName("@context")] public string Context { get; init; } = JsonLdMeta.ProfileContext;

    [JsonPropertyName("@type")] public string Type { get; init; } = "Profile";

    [JsonPropertyName("previewImageUrl")] public string? PreviewImageUrl { get; init; }
    [JsonPropertyName("imageUrl")] public string? ImageUrl { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;

    /// <summary>namespace → *head-of-index* CID</summary>
    [JsonPropertyName("namespaces")]
    public Dictionary<string, string> Namespaces { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("signingKeys")]
    public Dictionary<string, SigningKey> SigningKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}