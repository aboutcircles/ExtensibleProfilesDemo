using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;

namespace Circles.Profiles.Sdk.Utils;

/// <summary>
/// RFC 8785‑compatible canonical JSON *without* the <c>signature</c> field.
/// • Duplicate properties → <see cref="JsonException"/>  
/// • Numbers normalised to shortest‑round‑trip form  
/// • Stable across runtimes & cultures
/// </summary>
public static class CanonicalJson
{
    public static byte[] CanonicaliseWithoutSignature(CustomDataLink link)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(link));

        var buf = new ArrayBufferWriter<byte>();
        using var w = new Utf8JsonWriter(buf, new JsonWriterOptions { SkipValidation = true });

        Write(doc.RootElement, w, skipSignature: true);
        w.Flush();
        return buf.WrittenSpan.ToArray();
    }

    /* ───────────────────── helpers ───────────────────── */

    private static void Write(JsonElement el, Utf8JsonWriter w, bool skipSignature)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object: WriteObject(el, w, skipSignature); break;
            case JsonValueKind.Array: WriteArray(el, w, skipSignature); break;
            case JsonValueKind.String: w.WriteStringValue(el.GetString()); break;
            case JsonValueKind.Number: WriteNumber(el, w); break;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                w.WriteRawValue(el.GetRawText());
                break;
            default:
                throw new NotSupportedException($"unsupported kind {el.ValueKind}");
        }
    }

    private static void WriteObject(JsonElement obj, Utf8JsonWriter w, bool skipSignature)
    {
        w.WriteStartObject();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (skipSignature && p.NameEquals("signature")) continue;
            if (!seen.Add(p.Name))
                throw new JsonException($"duplicate property \"{p.Name}\"");

            w.WritePropertyName(p.Name);
            Write(p.Value, w, skipSignature);
        }

        w.WriteEndObject();
    }

    private static void WriteArray(JsonElement arr, Utf8JsonWriter w, bool skipSignature)
    {
        w.WriteStartArray();
        foreach (var itm in arr.EnumerateArray())
            Write(itm, w, skipSignature);
        w.WriteEndArray();
    }

    private static void WriteNumber(JsonElement el, Utf8JsonWriter w)
    {
        if (el.TryGetInt64(out long i))
        {
            w.WriteNumberValue(i);
            return;
        }

        if (el.TryGetDouble(out double d))
        {
            w.WriteNumberValue(d);
            return;
        }

        // lossless fallback for big integers
        var raw = el.GetRawText();
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec))
            throw new JsonException($"unsupported number format: {raw}");
        w.WriteNumberValue(dec);
    }
}