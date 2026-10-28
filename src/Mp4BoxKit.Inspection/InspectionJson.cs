using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mp4BoxKit.Inspection;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Mp4Inspection))]
internal partial class InspectionJsonContext : JsonSerializerContext
{
}

/// <summary>Serializes inspection results for CLI and WASM callers.</summary>
public static class InspectionJson
{
    public static string Serialize(Mp4Inspection inspection)
        => JsonSerializer.Serialize(inspection, InspectionJsonContext.Default.Mp4Inspection);

    public static InspectOptions DeserializeOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new InspectOptions();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new InspectOptions();
            }

            var defaults = new InspectOptions();
            return new InspectOptions
            {
                MaxDepth = ReadInt(root, "maxDepth", defaults.MaxDepth),
                MaxBoxes = ReadInt(root, "maxBoxes", defaults.MaxBoxes),
                IncludeBoxes = ReadBool(root, "includeBoxes", defaults.IncludeBoxes),
                IncludeSamples = ReadBool(root, "includeSamples", defaults.IncludeSamples),
                MaxSamples = ReadInt(root, "maxSamples", defaults.MaxSamples),
                IncludeSegments = ReadBool(root, "includeSegments", defaults.IncludeSegments),
                MaxSegments = ReadInt(root, "maxSegments", defaults.MaxSegments),
                MaxInputBytes = ReadLong(root, "maxInputBytes", defaults.MaxInputBytes)
            };
        }
        catch (JsonException)
        {
            return new InspectOptions();
        }
    }

    private static bool TryGetProperty(
        JsonElement root,
        string name,
        out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
    {
        if (!TryGetProperty(root, name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed))
        {
            return fallback;
        }

        return parsed;
    }

    private static long ReadLong(JsonElement root, string name, long fallback)
    {
        if (!TryGetProperty(root, name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var parsed))
        {
            return fallback;
        }

        return parsed;
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback)
    {
        if (!TryGetProperty(root, name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }
}
