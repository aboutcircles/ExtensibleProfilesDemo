using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Circles.Profiles.Models;

public sealed class Iso8601DateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset?>
{
    // RFC3339-compatible shapes (UTC Z or with offset), fractional seconds optional up to 7 digits.
    // Using 'K' means we accept either 'Z' or an explicit ±HH:mm offset.
    private static readonly string[] ReadFormats =
    {
        "yyyy'-'MM'-'dd'T'HH':'mm':'ssK",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK",
    };

    private const string WriteFormatWithFraction = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF'Z'";
    private const string WriteFormatNoFraction = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'";

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string for ISO-8601 timestamp, but found {reader.TokenType}.");
        }

        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(
                s,
                ReadFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var dto))
        {
            return dto.ToUniversalTime();
        }

        throw new JsonException($"Invalid ISO-8601 timestamp: '{s}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        var utc = value.Value.ToUniversalTime();

        var hasFraction = (utc.Ticks % TimeSpan.TicksPerSecond) != 0;
        var format = hasFraction ? WriteFormatWithFraction : WriteFormatNoFraction;

        writer.WriteStringValue(utc.ToString(format, CultureInfo.InvariantCulture));
    }
}

public sealed class Iso8601DateTimeOffsetNonNullJsonConverter : JsonConverter<DateTimeOffset>
{
    private readonly Iso8601DateTimeOffsetJsonConverter _inner = new();

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        var v = _inner.Read(ref reader, typeof(DateTimeOffset?), options);
        if (!v.HasValue)
        {
            throw new JsonException("Expected non-null ISO-8601 timestamp.");
        }

        return v.Value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value,
        System.Text.Json.JsonSerializerOptions options)
    {
        _inner.Write(writer, value, options);
    }
}