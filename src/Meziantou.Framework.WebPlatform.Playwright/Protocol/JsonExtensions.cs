using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Playwright.Protocol;

/// <summary>Reads the parameters of the protocol calls (a missing parameter object is an empty object).</summary>
internal static class JsonExtensions
{
    public static bool TryGet(this JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            return true;

        value = default;
        return false;
    }

    public static string? GetString(this JsonElement element, string name) => element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static string GetRequiredString(this JsonElement element, string name) => element.GetString(name) ?? throw new PlaywrightError($"Missing parameter '{name}'");

    public static double? GetDouble(this JsonElement element, string name) => element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    public static int? GetInt32(this JsonElement element, string name) => element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.Number ? (int)value.GetDouble() : null;

    public static bool? GetBoolean(this JsonElement element, string name) => element.TryGet(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    public static bool GetBoolean(this JsonElement element, string name, bool defaultValue) => element.GetBoolean(name) ?? defaultValue;

    public static byte[]? GetBinary(this JsonElement element, string name) => element.GetString(name) is { } base64 ? Convert.FromBase64String(base64) : null;

    public static IEnumerable<JsonElement> GetArray(this JsonElement element, string name)
    {
        if (!element.TryGet(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray();
    }

    /// <summary>The <c>guid</c> of an object reference (<c>{ "guid": "..." }</c>), or null.</summary>
    public static string? GetGuid(this JsonElement element, string name) => element.TryGet(name, out var value) ? value.GetString("guid") : null;

    /// <summary>Reads a list of name-value pairs (<c>[{ "name": ..., "value": ... }]</c>).</summary>
    public static List<KeyValuePair<string, string>> GetNameValues(this JsonElement element, string name)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var item in element.GetArray(name))
        {
            result.Add(new(item.GetString("name") ?? "", item.GetString("value") ?? ""));
        }

        return result;
    }

    public static JsonArray ToNameValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        var array = new JsonArray();
        foreach (var (name, value) in values)
        {
            array.Add((JsonNode)new JsonObject { ["name"] = name, ["value"] = value });
        }

        return array;
    }

    /// <summary>Converts a JSON element to a node (copies it).</summary>
    public static JsonNode? ToNode(this JsonElement element) => element.ValueKind is JsonValueKind.Undefined ? null : JsonNode.Parse(element.GetRawText());

    public static string ToInvariantString(this double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
