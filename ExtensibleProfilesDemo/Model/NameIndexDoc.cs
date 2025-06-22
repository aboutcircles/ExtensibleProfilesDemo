using System.Text.Json.Serialization;

namespace ExtensibleProfilesDemo.Model;

/// <summary>
/// One tiny, append-only index per namespace.<br/>
/// • <c>head</c>    – CID of the newest chunk <br/>
/// • <c>entries</c> – link-name → “owning chunk” CID
/// </summary>
public sealed record NameIndexDoc
{
    [JsonPropertyName("head")]
    public string Head { get; set; } = string.Empty;          // ← was init-only

    [JsonPropertyName("entries")]
    public Dictionary<string, string> Entries { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}