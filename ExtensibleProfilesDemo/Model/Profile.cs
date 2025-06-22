using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace ExtensibleProfilesDemo.Model;

public sealed record Profile
{
    [JsonPropertyName("schemaVersion")]   public string SchemaVersion   { get; init; } = "1.1";
    [JsonPropertyName("previewImageUrl")] public string? PreviewImageUrl { get; init; }

    [JsonPropertyName("name")]        public string Name        { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;

    // housekeeping -----------------------------------------------------------
    [JsonPropertyName("lastRead")]
    public Dictionary<string, long> LastRead { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    // namespace → HEAD-chunk CID
    [JsonPropertyName("namespaces")]
    public Dictionary<string, string> Namespaces { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    // authorised public keys --------------------------------------------------
    [JsonPropertyName("signingKeys")]
    public Dictionary<string, SigningKey> SigningKeys { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}