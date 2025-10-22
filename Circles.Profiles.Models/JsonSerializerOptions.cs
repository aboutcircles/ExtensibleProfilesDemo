using System.Text.Json.Serialization;

namespace Circles.Profiles.Models;

/// <summary>
/// Helper to create <see cref="JsonSerializerOptions"/> that work with the json-ld model at hand.
/// </summary>
public static class JsonSerializerOptions
{
    public static System.Text.Json.JsonSerializerOptions JsonLd => new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters =
        {
            new Iso8601DateTimeOffsetJsonConverter(),
            new Iso8601DateTimeOffsetNonNullJsonConverter(),
            new ImageRefJsonConverter()
        }
    };
}