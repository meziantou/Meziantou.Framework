using System.Text.Json;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Flattens a JSON object into rows of paths and values, as the JSON tables of the use case diagrams of Mermaid.</summary>
internal static class JsonTableRows
{
    public static List<(string Key, string Value)> Create(string json)
    {
        var rows = new List<(string Key, string Value)>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            Add(document.RootElement, string.Empty, rows);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // The data is validated when it is set; a table without rows is drawn otherwise
        }

        return rows;
    }

    private static void Add(JsonElement element, string path, List<(string Key, string Value)> rows)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    // A repeated property keeps its first position and its last value
                    var order = new List<string>();
                    var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!values.ContainsKey(property.Name))
                        {
                            order.Add(property.Name);
                        }

                        values[property.Name] = property.Value;
                    }

                    if (order.Count is 0)
                    {
                        rows.Add((path, "{}"));
                        return;
                    }

                    foreach (var name in order)
                    {
                        Add(values[name], path.Length is 0 ? name : path + "." + name, rows);
                    }

                    break;
                }

            case JsonValueKind.Array:
                {
                    var index = 0;
                    var first = true;
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            Add(item, path + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", rows);
                        }
                        else
                        {
                            // The values of an array of scalars are listed under its name
                            rows.Add((first ? path : string.Empty, Format(item)));
                        }

                        first = false;
                        index++;
                    }

                    if (index is 0)
                    {
                        rows.Add((path, "[]"));
                    }

                    break;
                }

            default:
                rows.Add((path, Format(element)));
                break;
        }
    }

    private static string Format(JsonElement element) => element.ValueKind is JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
}
