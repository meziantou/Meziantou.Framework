using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal sealed class JsonNodeDocument
{
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private JsonNodeDocument(JsonNode root)
    {
        Root = root;
    }

    public JsonNode Root { get; }

    public static async ValueTask<JsonNodeDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var root = await JsonNode.ParseAsync(stream, nodeOptions: null, documentOptions: JsonDocumentOptions, cancellationToken: cancellationToken).ConfigureAwait(false) ?? throw new JsonException("Expected a JSON value.");
        return new JsonNodeDocument(root);
    }

    public static JsonNode ParseNode(string text)
    {
        return JsonNode.Parse(text, nodeOptions: null, documentOptions: JsonDocumentOptions) ?? throw new JsonException("Expected a JSON value.");
    }

    public static string GetPath(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var components = new List<string>();
        var current = node;
        while (current.Parent is { } parent)
        {
            switch (parent)
            {
                case JsonObject jsonObject:
                    components.Add("[" + JsonSerializer.Serialize(GetPropertyName(jsonObject, current)) + "]");
                    break;
                case JsonArray jsonArray:
                    components.Add("[" + GetElementIndex(jsonArray, current).ToString(CultureInfo.InvariantCulture) + "]");
                    break;
                default:
                    throw new InvalidOperationException("Unexpected JSON node parent.");
            }

            current = parent;
        }

        components.Reverse();

        return "$" + string.Concat(components);
    }

    public JsonObject? GetRootObject()
    {
        return Root as JsonObject;
    }

    public static IEnumerable<(string Name, JsonNode? Value)> GetProperties(JsonObject jsonObject)
    {
        foreach (var property in jsonObject)
        {
            yield return (property.Key, property.Value);
        }
    }

    public static IEnumerable<JsonNode?> GetArray(JsonArray jsonArray)
    {
        foreach (var item in jsonArray)
        {
            yield return item;
        }
    }

    public static bool TryGetProperty(JsonObject jsonObject, string propertyName, out JsonNode? value)
    {
        return jsonObject.TryGetPropertyValue(propertyName, out value);
    }

    public static bool TryGetObject(JsonObject jsonObject, string propertyName, [NotNullWhen(true)] out JsonObject? value)
    {
        if (jsonObject.TryGetPropertyValue(propertyName, out var node) && node is JsonObject jsonObjectValue)
        {
            value = jsonObjectValue;
            return true;
        }

        value = null;
        return false;
    }

    public static bool TryGetArray(JsonObject jsonObject, string propertyName, [NotNullWhen(true)] out JsonArray? value)
    {
        if (jsonObject.TryGetPropertyValue(propertyName, out var node) && node is JsonArray jsonArrayValue)
        {
            value = jsonArrayValue;
            return true;
        }

        value = null;
        return false;
    }

    public static bool TryGetString(JsonNode? node, [NotNullWhen(true)] out string? value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            return true;
        }

        value = null;
        return false;
    }

    private static string GetPropertyName(JsonObject jsonObject, JsonNode node)
    {
        foreach (var property in jsonObject)
        {
            if (ReferenceEquals(property.Value, node))
                return property.Key;
        }

        throw new InvalidOperationException("The JSON node was not found in its parent object.");
    }

    private static int GetElementIndex(JsonArray jsonArray, JsonNode node)
    {
        for (var index = 0; index < jsonArray.Count; index++)
        {
            if (ReferenceEquals(jsonArray[index], node))
                return index;
        }

        throw new InvalidOperationException("The JSON node was not found in its parent array.");
    }
}
