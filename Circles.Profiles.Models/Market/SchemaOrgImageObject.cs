using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Market;

/// <summary>ImageObject helper for non-HTTP transports.</summary>
public sealed record SchemaOrgImageObject
{
    [JsonPropertyName("@type")] public string Type { get; init; } = "ImageObject";
    [JsonPropertyName("contentUrl")] public string ContentUrl { get; init; } = string.Empty; // ipfs://, ar://, data:
    [JsonPropertyName("url")] public string? Url { get; init; } // optional HTTP mirror
}

