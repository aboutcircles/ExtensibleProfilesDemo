using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Core;

/// <summary>
/// One “page” of a namespace.
/// • <c>prev</c> – CID of the next-older chunk (or <c>null</c> for the tail)
/// • <c>links</c> – chronologically stored <see cref="CustomDataLink"/>s
/// </summary>
public sealed record NamespaceChunk
{
    [JsonPropertyName("@context")] public string Context { get; init; } = JsonLdMeta.NamespaceContext;

    [JsonPropertyName("@type")] public string Type { get; init; } = "NamespaceChunk";

    [JsonPropertyName("prev")] public string? Prev { get; init; }
    [JsonPropertyName("links")] public List<CustomDataLink> Links { get; init; } = new();
}