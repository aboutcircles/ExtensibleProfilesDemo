using System.Buffers;
using System.Text.Json;
using ExtensibleProfilesDemo.Model;

namespace ExtensibleProfilesDemo;

internal static class CanonicalJson
{
    /// <summary>
    /// RFC 8785-compatible canonical UTF-8 **without** the “signature” field.
    /// </summary>
    internal static byte[] CanonicaliseWithoutSignature(CustomDataLink link)
    {
        // serialise once – we'll stream it back out immediately
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(link));

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            SkipValidation = true // our input is already valid JSON
        });

        WriteElement(doc.RootElement, writer, skipSignature: true);
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteElement(JsonElement el, Utf8JsonWriter w, bool skipSignature)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(el, w, skipSignature);
                break;

            case JsonValueKind.Array:
                WriteArray(el, w, skipSignature);
                break;

            case JsonValueKind.String:
                w.WriteStringValue(el.GetString());
                break;

            case JsonValueKind.Number:
                w.WriteRawValue(el.GetRawText());
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                w.WriteRawValue(el.GetRawText());
                break;

            default:
                throw new NotSupportedException($"Unsupported kind {el.ValueKind}");
        }
    }

    private static void WriteObject(JsonElement obj, Utf8JsonWriter w, bool skipSignature)
    {
        w.WriteStartObject();

        foreach (var prop in obj.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (skipSignature && prop.NameEquals("signature"))
            {
                continue;
            }

            w.WritePropertyName(prop.Name); // already escaped
            WriteElement(prop.Value, w, skipSignature);
        }

        w.WriteEndObject();
    }

    private static void WriteArray(JsonElement arr, Utf8JsonWriter w, bool skipSignature)
    {
        w.WriteStartArray();
        foreach (var item in arr.EnumerateArray())
        {
            WriteElement(item, w, skipSignature);
        }

        w.WriteEndArray();
    }
}