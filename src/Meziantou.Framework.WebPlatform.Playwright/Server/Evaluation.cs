using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Playwright.Dispatchers;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>
/// The evaluations of the clients (<c>evaluate()</c> and <c>evaluateHandle()</c>, <c>javascript.ts</c> of the Playwright
/// server): the expression and its argument (a serialized value whose handles are objects of the page) are given to the utility
/// script, which parses the argument, calls the function, awaits the result and serializes it.
/// </summary>
internal static partial class Evaluation
{
    private const string EvaluateFunction = "(utilityScript, ...args) => utilityScript.evaluate(...args)";

    /// <summary>Normalizes the expression of an evaluation (<c>normalizeEvaluationExpression</c>): a method shorthand becomes a function.</summary>
    public static string NormalizeExpression(string expression, bool? isFunction)
    {
        expression = expression.Trim();
        if (isFunction == true && !IsValidExpression(expression))
        {
            expression = expression.StartsWith("async ", StringComparison.Ordinal) ? "async function " + expression["async ".Length..] : "function " + expression;
            if (!IsValidExpression(expression))
                throw new PlaywrightError("Passed function is not well-serializable!");
        }

        if (FunctionRegex().IsMatch(expression))
        {
            expression = "(" + expression + ")";
        }

        return expression;

        static bool IsValidExpression(string expression) => JsPreparedScript.TryParse("(" + expression + ")", url: null, 0, 0, out _, out _);
    }

    [GeneratedRegex(@"^(async)?\s*function(\s|\()", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FunctionRegex();

    /// <summary>Evaluates an expression and returns its serialized value (<c>returnByValue</c>).</summary>
    public static async Task<JsonNode> EvaluateAsync(FrameExecutionContext context, string expression, bool? isFunction, JsonElement argument, PlaywrightConnection connection, Progress progress, JSHandleDispatcher? thisHandle = null)
    {
        var result = await CallUtilityEvaluateAsync(context, expression, isFunction, returnByValue: true, argument, connection, progress, thisHandle).ConfigureAwait(true);
        return ToSerializedValue(context, result);
    }

    /// <summary>Evaluates an expression and returns its value (a handle is created by the caller).</summary>
    public static Task<JsValue> EvaluateHandleAsync(FrameExecutionContext context, string expression, bool? isFunction, JsonElement argument, PlaywrightConnection connection, Progress progress, JSHandleDispatcher? thisHandle = null)
        => CallUtilityEvaluateAsync(context, expression, isFunction, returnByValue: false, argument, connection, progress, thisHandle);

    /// <summary>Converts the serialized value returned by the utility script (an object of the page) to JSON (<c>undefined</c> is <c>{ "v": "undefined" }</c>).</summary>
    public static JsonNode ToSerializedValue(FrameExecutionContext context, JsValue serialized)
    {
        if (serialized.IsUndefined)
            return new JsonObject { ["v"] = "undefined" };

        var json = context.Stringify(serialized);
        return json is null ? new JsonObject { ["v"] = "undefined" } : PageToWire(JsonNode.Parse(json));
    }

    /// <summary>
    /// Converts a value serialized by the utility script (<c>serializeAsCallArgument</c>: the primitives are not wrapped) to the
    /// serialized value of the protocol (<c>{ "n": 1 }</c>, <c>{ "s": "text" }</c>...).
    /// </summary>
    public static JsonNode PageToWire(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return new JsonObject { ["v"] = "null" };

            case JsonValue value when value.TryGetValue<bool>(out var boolean):
                return new JsonObject { ["b"] = boolean };

            case JsonValue value when value.TryGetValue<string>(out var text):
                return new JsonObject { ["s"] = text };

            case JsonValue value:
                return new JsonObject { ["n"] = value.GetValue<double>() };

            case JsonArray array:
                return new JsonObject { ["a"] = new JsonArray([.. array.Select(PageToWire)]) };

            case JsonObject obj:
                if (obj["a"] is JsonArray items)
                    return new JsonObject { ["a"] = new JsonArray([.. items.Select(PageToWire)]), ["id"] = obj["id"]?.DeepClone() };

                if (obj["o"] is JsonArray properties)
                    return new JsonObject { ["o"] = new JsonArray([.. properties.Select(p => (JsonNode)new JsonObject { ["k"] = p!["k"]!.DeepClone(), ["v"] = PageToWire(p["v"]) })]), ["id"] = obj["id"]?.DeepClone() };

                if (obj["ab"] is JsonObject arrayBuffer)
                    return new JsonObject { ["ta"] = new JsonObject { ["b"] = arrayBuffer["b"]?.DeepClone(), ["k"] = "ui8" } };

                // The maps and the sets are not serialized by the protocol (they are serialized as objects)
                if (obj["m"] is JsonArray || obj["s"] is JsonArray)
                    return new JsonObject { ["o"] = new JsonArray(), ["id"] = obj["id"]?.DeepClone() };

                return obj.DeepClone();

            default:
                return node.DeepClone();
        }
    }

    /// <summary>Converts a serialized value of the protocol (an argument of the client) to the serialization of the utility script.</summary>
    public static JsonNode? WireToPage(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return node?.DeepClone();

        if (obj.TryGetPropertyValue("n", out var number))
            return number?.DeepClone();

        if (obj.TryGetPropertyValue("b", out var boolean))
            return boolean?.DeepClone();

        if (obj.TryGetPropertyValue("s", out var text))
            return text?.DeepClone();

        if (obj["a"] is JsonArray items)
            return new JsonObject { ["a"] = new JsonArray([.. items.Select(WireToPage)]), ["id"] = obj["id"]?.DeepClone() };

        if (obj["o"] is JsonArray properties)
            return new JsonObject { ["o"] = new JsonArray([.. properties.Select(p => (JsonNode)new JsonObject { ["k"] = p!["k"]!.DeepClone(), ["v"] = WireToPage(p["v"]) })]), ["id"] = obj["id"]?.DeepClone() };

        return obj.DeepClone();
    }

    /// <summary>Serializes a value of the page (<c>serializeAsCallArgument</c> of the utility script).</summary>
    public static JsonNode SerializeValue(FrameExecutionContext context, JsValue value)
    {
        var serialized = context.Call("(utilityScript, value) => utilityScript.jsonValue(true, value)", context.UtilityScript, value);
        return ToSerializedValue(context, serialized);
    }

    private static async Task<JsValue> CallUtilityEvaluateAsync(FrameExecutionContext context, string expression, bool? isFunction, bool returnByValue, JsonElement argument, PlaywrightConnection connection, Progress progress, JSHandleDispatcher? thisHandle)
    {
        expression = NormalizeExpression(expression, isFunction);
        var handles = new List<JsValue>();
        foreach (var handleReference in argument.GetArray("handles"))
        {
            var guid = handleReference.GetString("guid") ?? throw new PlaywrightError("Invalid handle");
            var handle = connection.Find(guid) as JSHandleDispatcher ?? throw new PlaywrightError("JSHandle is disposed!");
            if (!ReferenceEquals(handle.Context, context))
                throw new PlaywrightError("JSHandles can be evaluated only in the context they were created!");

            handles.Add(handle.Value);
        }

        var argumentValue = argument.TryGet("value", out var value) ? context.ParseJson(WireToPage(JsonNode.Parse(value.GetRawText()))?.ToJsonString() ?? "null") : context.ParseJson("{\"v\":\"undefined\"}");
        var arguments = new List<JsValue>
        {
            context.UtilityScript,
            isFunction is { } function ? function : JsValue.Undefined,
            returnByValue,
            expression,
        };
        if (thisHandle is not null)
        {
            if (!ReferenceEquals(thisHandle.Context, context))
                throw new PlaywrightError("JSHandles can be evaluated only in the context they were created!");

            arguments.Add(2);
            arguments.Add(context.ParseJson("{\"h\":" + handles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"));
            arguments.Add(argumentValue);
            handles.Add(thisHandle.Value);
        }
        else
        {
            arguments.Add(1);
            arguments.Add(argumentValue);
        }

        arguments.AddRange(handles);
        try
        {
            var result = context.Call(EvaluateFunction, [.. arguments]);
            return await context.AwaitAsync(result, progress).ConfigureAwait(true);
        }
        catch (JsException ex)
        {
            throw context.ToPlaywrightError(ex);
        }
    }
}
