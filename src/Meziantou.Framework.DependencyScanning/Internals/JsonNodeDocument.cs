using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal sealed class JsonNodeDocument
{
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,

        // Otherwise a duplicate key is only reported when the object is first read, as an ArgumentException
        AllowDuplicateProperties = false,
    };

    private JsonNodeDocument(JsonNode root)
    {
        Root = root;
    }

    public JsonNode Root { get; }

    /// <summary>Parses a JSON document, reporting any malformed content as a <see cref="JsonException"/>.</summary>
    /// <remarks>
    /// System.Text.Json only reports invalid strings when they are read. Everything is validated here instead, so that a
    /// scanner only has to handle <see cref="JsonException"/>.
    /// </remarks>
    /// <exception cref="JsonException">The document is not valid JSON.</exception>
    public static async ValueTask<JsonNodeDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var encoding = await StreamUtilities.GetEncodingAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);

        // System.Text.Json only reads UTF-8, so anything else has to be transcoded first
        if (encoding.CodePage != Encoding.UTF8.CodePage)
        {
            using var reader = StreamUtilities.CreateReader(stream, encoding);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return new JsonNodeDocument(ParseNode(text));
        }

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        var utf8Json = memoryStream.GetBuffer().AsSpan(0, (int)memoryStream.Length);
        if (utf8Json is [0xEF, 0xBB, 0xBF, ..])
        {
            utf8Json = utf8Json[3..];
        }

        return new JsonNodeDocument(ParseUtf8(utf8Json));
    }

    /// <exception cref="JsonException">The document is not valid JSON.</exception>
    public static JsonNode ParseNode(string text)
    {
        // Encoding replaces lone surrogates, so the UTF-8 bytes are always valid
        return ParseUtf8(Encoding.UTF8.GetBytes(text));
    }

    private static JsonNode ParseUtf8(ReadOnlySpan<byte> utf8Json)
    {
        if (!Utf8.IsValid(utf8Json))
            throw new JsonException("The JSON document is not valid UTF-8.");

        ValidateEscapedStrings(utf8Json);
        return JsonNode.Parse(utf8Json, nodeOptions: null, documentOptions: JsonDocumentOptions) ?? throw new JsonException("Expected a JSON value.");
    }

    /// <summary>Ensures every escaped string decodes, which an escaped lone surrogate such as <c>"\uD800"</c> does not.</summary>
    private static void ValidateEscapedStrings(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IndexOf("\\u"u8) < 0)
            return;

        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            AllowTrailingCommas = JsonDocumentOptions.AllowTrailingCommas,
            CommentHandling = JsonDocumentOptions.CommentHandling,
            MaxDepth = JsonDocumentOptions.MaxDepth,
        });

        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && reader.ValueIsEscaped)
                {
                    _ = reader.GetString();
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException("The JSON document contains a string that cannot be decoded.", ex);
        }
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
