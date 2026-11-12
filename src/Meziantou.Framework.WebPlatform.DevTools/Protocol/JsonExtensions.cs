using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.DevTools.Protocol;

/// <summary>Reads the parameters of the commands (a missing parameter object is an empty object).</summary>
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

    public static string GetRequiredString(this JsonElement element, string name) => element.GetString(name) ?? throw ProtocolException.InvalidParameters(name + ": string value expected");

    public static double? GetDouble(this JsonElement element, string name) => element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    public static double GetRequiredDouble(this JsonElement element, string name) => element.GetDouble(name) ?? throw ProtocolException.InvalidParameters(name + ": double value expected");

    public static int? GetInt32(this JsonElement element, string name)
    {
        if (!element.TryGet(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;

        var number = value.GetDouble();
        return double.IsFinite(number) && number is >= int.MinValue and <= int.MaxValue ? (int)number : null;
    }

    public static int GetRequiredInt32(this JsonElement element, string name) => element.GetInt32(name) ?? throw ProtocolException.InvalidParameters(name + ": integer value expected");

    public static bool? GetBoolean(this JsonElement element, string name) => element.TryGet(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    public static bool GetBoolean(this JsonElement element, string name, bool defaultValue) => element.GetBoolean(name) ?? defaultValue;

    public static IEnumerable<JsonElement> GetArray(this JsonElement element, string name)
    {
        if (!element.TryGet(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray();
    }

    /// <summary>Reads the headers of the protocol: an object whose properties are the headers (<c>{ "Accept": "*/*" }</c>).</summary>
    public static List<KeyValuePair<string, string>> GetHeaderObject(this JsonElement element, string name)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (!element.TryGet(name, out var headers) || headers.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in headers.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();

            // A header whose value has several lines is several headers
            foreach (var line in value.Split('\n'))
            {
                result.Add(new(property.Name, line));
            }
        }

        return result;
    }

    /// <summary>Reads the headers of the protocol given as entries (<c>[{ "name": ..., "value": ... }]</c>).</summary>
    public static List<KeyValuePair<string, string>>? GetHeaderEntries(this JsonElement element, string name)
    {
        if (!element.TryGet(name, out var headers) || headers.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<KeyValuePair<string, string>>();
        foreach (var header in headers.EnumerateArray())
        {
            result.Add(new(header.GetString("name") ?? "", header.GetString("value") ?? ""));
        }

        return result;
    }

    /// <summary>Converts headers to the object of the protocol: the values of the headers of the same name are joined with a new line.</summary>
    public static JsonObject ToHeaderObject(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var result = new JsonObject();
        foreach (var (name, value) in headers)
        {
            var existing = result.FirstOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing.Key is not null)
            {
                result[existing.Key] = existing.Value!.GetValue<string>() + "\n" + value;
            }
            else
            {
                result[name] = value;
            }
        }

        return result;
    }

    public static string ToInvariantString(this double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
