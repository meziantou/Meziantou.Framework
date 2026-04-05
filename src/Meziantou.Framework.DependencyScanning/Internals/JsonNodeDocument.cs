using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal sealed class JsonNodeDocument
{
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonReaderOptions JsonReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly IReadOnlyDictionary<string, LineInfo> _lineInfos;

    private JsonNodeDocument(JsonNode root, IReadOnlyDictionary<string, LineInfo> lineInfos)
    {
        Root = root;
        _lineInfos = lineInfos;
    }

    public JsonNode Root { get; }

    public static async ValueTask<JsonNodeDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(stream, cancellationToken).ConfigureAwait(false);
        var text = await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var root = JsonNode.Parse(text, nodeOptions: null, documentOptions: JsonDocumentOptions) ?? throw new JsonException("Expected a JSON value.");
        return new JsonNodeDocument(root, CreateLineInfos(text));
    }

    public static JsonNode ParseNode(string text)
    {
        return JsonNode.Parse(text, nodeOptions: null, documentOptions: JsonDocumentOptions) ?? throw new JsonException("Expected a JSON value.");
    }

    public JsonObject? GetRootObject()
    {
        return Root as JsonObject;
    }

    public IEnumerable<(string Name, JsonNode? Value, string Path)> GetProperties(JsonObject jsonObject, string path)
    {
        foreach (var property in jsonObject)
        {
            var propertyPath = AppendPropertyPath(path, property.Key);
            yield return (property.Key, property.Value, propertyPath);
        }
    }

    public IEnumerable<(JsonNode? Value, int Index, string Path)> GetArray(JsonArray jsonArray, string path)
    {
        for (var index = 0; index < jsonArray.Count; index++)
        {
            var itemPath = AppendArrayIndexPath(path, index);
            yield return (jsonArray[index], index, itemPath);
        }
    }

    public bool TryGetProperty(JsonObject jsonObject, string propertyName, string path, out JsonNode? value, out string propertyPath)
    {
        propertyPath = AppendPropertyPath(path, propertyName);
        return jsonObject.TryGetPropertyValue(propertyName, out value);
    }

    public bool TryGetObject(JsonObject jsonObject, string propertyName, string path, [NotNullWhen(true)] out JsonObject? value, out string propertyPath)
    {
        propertyPath = AppendPropertyPath(path, propertyName);
        if (jsonObject.TryGetPropertyValue(propertyName, out var node) && node is JsonObject jsonObjectValue)
        {
            value = jsonObjectValue;
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetArray(JsonObject jsonObject, string propertyName, string path, [NotNullWhen(true)] out JsonArray? value, out string propertyPath)
    {
        propertyPath = AppendPropertyPath(path, propertyName);
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

    public LineInfo GetLineInfo(string path)
    {
        if (_lineInfos.TryGetValue(path, out var lineInfo))
            return lineInfo;

        return default;
    }

    private static string AppendPropertyPath(string path, string propertyName)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{path}['{EscapePropertyName(propertyName)}']");
    }

    private static string AppendArrayIndexPath(string path, int index)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");
    }

    private static Dictionary<string, LineInfo> CreateLineInfos(string json)
    {
        var result = new Dictionary<string, LineInfo>(StringComparer.Ordinal);
        var utf8Bytes = Encoding.UTF8.GetBytes(json);
        var lineStarts = GetLineStarts(utf8Bytes);
        var reader = new Utf8JsonReader(utf8Bytes, JsonReaderOptions);

        var pathComponents = new List<PathComponent>();
        var contexts = new Stack<ContainerContext>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (contexts.TryPeek(out var context))
                {
                    context.PendingPropertyName = reader.GetString();
                }

                continue;
            }

            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                var context = contexts.Pop();
                if (context.HasPathComponent)
                {
                    pathComponents.RemoveAt(pathComponents.Count - 1);
                }

                continue;
            }

            var hasPathComponent = AppendPathComponent(pathComponents, contexts);
            var path = BuildPath(pathComponents);
            var startOffset = reader.TokenStartIndex + (reader.TokenType == JsonTokenType.String ? 1 : 0);
            result[path] = GetLineInfo(utf8Bytes, lineStarts, startOffset);

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                contexts.Push(new ContainerContext
                {
                    IsObject = reader.TokenType == JsonTokenType.StartObject,
                    HasPathComponent = hasPathComponent,
                });
            }
            else if (hasPathComponent)
            {
                pathComponents.RemoveAt(pathComponents.Count - 1);
            }
        }

        return result;
    }

    private static bool AppendPathComponent(List<PathComponent> pathComponents, Stack<ContainerContext> contexts)
    {
        if (!contexts.TryPeek(out var context))
            return false;

        if (context.IsObject)
        {
            if (context.PendingPropertyName is null)
                return false;

            pathComponents.Add(PathComponent.FromName(context.PendingPropertyName));
            context.PendingPropertyName = null;
            return true;
        }

        pathComponents.Add(PathComponent.FromIndex(context.NextArrayIndex));
        context.NextArrayIndex++;
        return true;
    }

    private static List<int> GetLineStarts(byte[] data)
    {
        var result = new List<int>(capacity: 16) { 0 };
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '\n')
            {
                result.Add(i + 1);
            }
        }

        return result;
    }

    private static LineInfo GetLineInfo(byte[] data, List<int> lineStarts, long byteIndex)
    {
        var index = (int)Math.Min(byteIndex, data.Length);
        var lineIndex = lineStarts.BinarySearch(index);
        if (lineIndex < 0)
        {
            lineIndex = ~lineIndex - 1;
        }

        var lineStart = lineStarts[lineIndex];
        var byteCount = Math.Max(index - lineStart, 0);
        var linePosition = Encoding.UTF8.GetCharCount(data, lineStart, byteCount) + 1;
        return new LineInfo(lineIndex + 1, linePosition);
    }

    private static string BuildPath(List<PathComponent> pathComponents)
    {
        if (pathComponents.Count == 0)
            return "$";

        var sb = new StringBuilder();
        sb.Append('$');
        foreach (var component in pathComponents)
        {
            if (component.IsIndex)
            {
                sb.Append('[');
                sb.Append(component.Index);
                sb.Append(']');
            }
            else
            {
                sb.Append("['");
                sb.Append(EscapePropertyName(component.Name!));
                sb.Append("']");
            }
        }

        return sb.ToString();
    }

    private static string EscapePropertyName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            switch (ch)
            {
                case '\'':
                    sb.Append("\\'");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (ch < '\x20')
                    {
                        sb.Append("\\u00");
                        sb.Append(((int)ch).ToString("x2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    private sealed class ContainerContext
    {
        public bool IsObject { get; set; }
        public bool HasPathComponent { get; set; }
        public long NextArrayIndex { get; set; }
        public string? PendingPropertyName { get; set; }
    }

    private readonly struct PathComponent
    {
        private PathComponent(long index)
        {
            IsIndex = true;
            Index = index;
            Name = null;
        }

        private PathComponent(string name)
        {
            IsIndex = false;
            Index = 0;
            Name = name;
        }

        public bool IsIndex { get; }
        public long Index { get; }
        public string? Name { get; }

        public static PathComponent FromIndex(long index) => new(index);
        public static PathComponent FromName(string name) => new(name);
    }
}
