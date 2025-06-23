using System.Text.Json.Serialization;

namespace Circles.Profiles.Models;

/// <summary>
/// One “page” of a namespace.
/// • <c>prev</c>   – CID of the next-older chunk (or <c>null</c> for the tail)  
/// • <c>links</c>  – chronologically stored <see cref="CustomDataLink"/>s  
/// • <c>index</c>  – *new*: fast name → “this chunk” CID lookup
/// </summary>
public sealed record NamespaceChunk
{
    [JsonPropertyName("prev")] public string? Prev { get; init; }

    [JsonPropertyName("links")] public List<CustomDataLink> Links { get; init; } = new();
}