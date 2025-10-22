using System.Text.Json;
using System.Text.Json.Serialization;
using Circles.Profiles.Models.Market;

namespace Circles.Profiles.Models;

public sealed class ImageRefJsonConverter : JsonConverter<ImageRef>
{
    public override ImageRef Read(ref Utf8JsonReader reader, Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        var isString = reader.TokenType == JsonTokenType.String;
        var isStartObject = reader.TokenType == JsonTokenType.StartObject;

        if (!isString && !isStartObject)
        {
            throw new JsonException($"Expected string or object for ImageRef, but found {reader.TokenType}.");
        }

        if (isString)
        {
            var s = reader.GetString();
            var isEmpty = string.IsNullOrWhiteSpace(s);
            if (isEmpty)
            {
                throw new JsonException("ImageRef string must not be empty.");
            }

            var canParseAbsolute = Uri.TryCreate(s, UriKind.Absolute, out var uri);
            if (!canParseAbsolute)
            {
                throw new JsonException($"ImageRef string is not a valid absolute URI: '{s}'.");
            }

            return new ImageRef { Url = uri, Object = null };
        }

        using (var doc = JsonDocument.ParseValue(ref reader))
        {
            var root = doc.RootElement;

            // Optionally validate @type when present
            var hasType = root.TryGetProperty("@type", out var typeProp);
            var typeValue = hasType ? typeProp.GetString() : null;
            var typeLooksRight = !hasType || string.Equals(typeValue, "ImageObject", StringComparison.Ordinal);

            var hasContentUrl = root.TryGetProperty("contentUrl", out _);
            var hasUrl = root.TryGetProperty("url", out _);
            var shapeLooksRight = hasContentUrl || hasUrl;

            var isLikelyImageObject = typeLooksRight || shapeLooksRight;

            if (!isLikelyImageObject)
            {
                throw new JsonException("Object value for ImageRef does not look like a schema.org ImageObject.");
            }

            var obj = root.Deserialize<SchemaOrgImageObject>(options);
            var isNull = obj is null;
            if (isNull)
            {
                throw new JsonException("Failed to deserialize ImageObject for ImageRef.");
            }

            return new ImageRef { Url = null, Object = obj };
        }
    }

    public override void Write(Utf8JsonWriter writer, ImageRef value, System.Text.Json.JsonSerializerOptions options)
    {
        var hasUrl = value.Url is not null;
        var hasObject = value.Object is not null;

        var isAmbiguous = hasUrl && hasObject;
        var isEmpty = !hasUrl && !hasObject;

        if (isAmbiguous)
        {
            throw new JsonException("ImageRef has both Url and Object set; exactly one must be provided.");
        }

        if (isEmpty)
        {
            throw new JsonException("ImageRef has neither Url nor Object set.");
        }

        if (hasUrl)
        {
            writer.WriteStringValue(value.Url!.ToString());
            return;
        }

        JsonSerializer.Serialize(writer, value.Object, options);
    }
}