using System.Text.Json;

namespace Mizon.Windows.Mcp.Tests;

// Narrow assertion helper for the source-generated output schema, not a general
// JSON Schema implementation. Unsupported schema keywords aren't silently used
// as evidence that the result was validated.
internal static class SchemaAssertions
{
    public static void Conforms(JsonElement value, JsonElement schema) => Validate(value, schema, schema);

    private static void Validate(JsonElement value, JsonElement schema, JsonElement root)
    {
        if (schema.ValueKind == JsonValueKind.True) return;
        Check.That(schema.ValueKind == JsonValueKind.Object, "Unexpected schema node.");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var path = reference.GetString()!;
            Check.That(path.StartsWith("#/", StringComparison.Ordinal), "Only local schema references are supported by this check.");
            var target = root;
            foreach (var segment in path[2..].Split('/'))
                target = target.GetProperty(segment.Replace("~1", "/").Replace("~0", "~"));
            Validate(value, target, root);
            return;
        }
        Check.That(!schema.TryGetProperty("anyOf", out _) && !schema.TryGetProperty("oneOf", out _), "Unexpected schema union requires an explicit test update.");
        if (schema.TryGetProperty("type", out var type))
        {
            var matches = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Any(item => MatchesType(value, item.GetString()!))
                : MatchesType(value, type.GetString()!);
            Check.That(matches, "Structured result does not conform to its declared JSON type.");
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        if (schema.TryGetProperty("required", out var required))
            foreach (var property in required.EnumerateArray())
                Check.That(value.TryGetProperty(property.GetString()!, out _), "Structured result is missing a required property.");
        if (!schema.TryGetProperty("properties", out var properties)) return;
        foreach (var property in value.EnumerateObject())
        {
            if (properties.TryGetProperty(property.Name, out var childSchema)) Validate(property.Value, childSchema, root);
            else Check.That(!schema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False,
                "Structured result contains an undeclared property.");
        }
    }

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };
}
