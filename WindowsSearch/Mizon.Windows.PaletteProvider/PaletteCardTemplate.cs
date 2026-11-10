using System.Text;
using System.Text.Json;

namespace Mizon.Windows.PaletteProvider;

// The installed Windows renderer can parse a partial card when a binding such
// as isVisible arrives as a string. Supply a complete, typed card document so
// its layout does not depend on the host's template-expansion configuration.
internal static class PaletteCardTemplate
{
    internal static string Expand(string template, string data)
    {
        using var source = JsonDocument.Parse(template);
        using var values = JsonDocument.Parse(data);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(source.RootElement, values.RootElement, writer);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static void Write(JsonElement node, JsonElement data, Utf8JsonWriter writer)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in node.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, data, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in node.EnumerateArray())
                    if (IsVisible(child, data)) Write(child, data, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = node.GetString()!;
                if (text.StartsWith("${", StringComparison.Ordinal) && text.EndsWith('}'))
                {
                    if (!data.TryGetProperty(text[2..^1], out var value))
                        throw new InvalidDataException("A preview template field has no value.");
                    value.WriteTo(writer);
                }
                else writer.WriteStringValue(text);
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static bool IsVisible(JsonElement node, JsonElement data)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("isVisible", out var visible)) return true;
        if (visible.ValueKind == JsonValueKind.String)
        {
            var expression = visible.GetString()!;
            if (expression.StartsWith("${", StringComparison.Ordinal) && expression.EndsWith('}') &&
                data.TryGetProperty(expression[2..^1], out var value)) visible = value;
        }
        // The Windows renderer validates empty text/images before it honors
        // visibility. Omit inactive branches instead of handing it invalid
        // empty controls; a state change rebuilds the complete document.
        return visible.ValueKind != JsonValueKind.False;
    }
}
