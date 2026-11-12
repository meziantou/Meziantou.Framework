using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The JSON serialization of the JavaScript values (https://infra.spec.whatwg.org/#serialize-a-javascript-value-to-a-json-string
// and https://infra.spec.whatwg.org/#parse-a-json-string-to-a-javascript-value), e.g. the schemas and the arguments of the
// WebMCP tools
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private TValue? _jsonStringify;

    /// <summary>
    /// Serializes a value to a JSON string with the original <c>JSON.stringify()</c> of the realm: null when it returns
    /// <c>undefined</c>. The exceptions thrown by <c>JSON.stringify()</c> (e.g. a circular reference, a BigInt, a
    /// <c>toJSON()</c> method that throws) propagate.
    /// </summary>
    internal string? SerializeJson(object? value)
    {
        switch (value)
        {
            case JsonNode node:
                return node.ToJsonString();
            case JsonElement element:
                return element.GetRawText();
        }

        _jsonStringify ??= EvaluateBuiltIn("JSON.stringify");
        var jsValue = ToJs(value);
        var result = Adapter.RunInRealm(EngineRealm, () => Adapter.Call(_jsonStringify, Adapter.Undefined, [jsValue]));
        return Adapter.IsUndefined(result) ? null : Adapter.ToString(result);
    }

    /// <summary>Parses a JSON string with the original <c>JSON.parse()</c> of the realm. The exceptions of <c>JSON.parse()</c> propagate.</summary>
    internal object? ParseJson(string json, out bool isObject)
    {
        _jsonParse ??= EvaluateBuiltIn("JSON.parse");
        var result = Adapter.RunInRealm(EngineRealm, () => Adapter.Call(_jsonParse, Adapter.Undefined, [Adapter.FromString(json)]));
        isObject = Adapter.IsObject(result);
        return result;
    }
}
