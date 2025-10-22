using System.Text.Json.Serialization;

namespace Circles.Profiles.Models.Chat;

public sealed record BasicMessage
{
    [JsonPropertyName("@context")] public string Context { get; init; } = JsonLdMeta.ChatContext;

    [JsonPropertyName("@type")] public string Type { get; init; } = "BasicMessage";
    [JsonPropertyName("from")] public string From { get; init; } = string.Empty;
    [JsonPropertyName("to")] public string To { get; init; } = string.Empty;
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
    [JsonPropertyName("ts")] public long Timestamp { get; init; }
}