using System.Text.Json.Serialization;

namespace ExtensibleProfilesDemo.Model;

public sealed record ChatMessage
{
    [JsonPropertyName("from")] public string From { get; init; } = string.Empty;
    [JsonPropertyName("to")] public string To { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
    [JsonPropertyName("ts")] public long Timestamp { get; init; }
}