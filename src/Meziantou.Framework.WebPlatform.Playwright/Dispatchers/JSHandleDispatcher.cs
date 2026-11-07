using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>A handle to a value of a page (<c>JSHandle</c>): the value is kept alive until the handle is disposed.</summary>
internal class JSHandleDispatcher : Dispatcher
{
    public JSHandleDispatcher(FrameDispatcher frame, FrameExecutionContext context, JsValue value, string preview)
        : this(frame, "JSHandle", "handle", context, value, preview)
    {
    }

    protected JSHandleDispatcher(FrameDispatcher frame, string type, string guidPrefix, FrameExecutionContext context, JsValue value, string preview)
        : base(frame, type, guidPrefix, new JsonObject { ["preview"] = preview })
    {
        Frame = frame;
        Context = context;
        Value = value;
    }

    public FrameDispatcher Frame { get; }

    public FrameExecutionContext Context { get; }

    public JsValue Value { get; }

    protected BrowserContextDispatcher BrowserContext => Frame.Page.Context;

    /// <summary>Returns the preview of a value that is not an element (<c>JSHandle@object</c>, or the string of a primitive).</summary>
    public static string GetPreview(FrameExecutionContext context, JsValue value)
    {
        if (!value.IsObject)
            return value.IsUndefined ? "undefined" : value.IsNull ? "null" : context.Call("(v) => String(v)", value).AsString();

        return context.Call("(v) => Array.isArray(v) ? 'JSHandle@array' : typeof v === 'function' ? 'JSHandle@function' : v instanceof Promise ? 'JSHandle@promise' : v instanceof Error ? 'JSHandle@error' : 'JSHandle@object'", value).AsString();
    }

    public override async ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        switch (method)
        {
            case "dispose":
                Dispose();
                return null;

            case "evaluateExpression":
                return await BrowserContext.Engine.InvokeAsync(async () => new JsonObject
                {
                    ["value"] = await Evaluation.EvaluateAsync(Context, parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"), GetArgument(parameters), Connection, progress, this).ConfigureAwait(true),
                }).ConfigureAwait(false);

            case "evaluateExpressionHandle":
                return await BrowserContext.Engine.InvokeAsync(async () =>
                {
                    var value = await Evaluation.EvaluateHandleAsync(Context, parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"), GetArgument(parameters), Connection, progress, this).ConfigureAwait(true);
                    return new JsonObject { ["handle"] = Frame.CreateHandle(Context, value).ToReference() };
                }).ConfigureAwait(false);

            case "getProperty":
                return await BrowserContext.Engine.InvokeAsync(() =>
                {
                    var value = Context.Call("(o, name) => o[name]", Value, parameters.GetRequiredString("name"));
                    return new JsonObject { ["handle"] = Frame.CreateHandle(Context, value).ToReference() };
                }).ConfigureAwait(false);

            case "getPropertyList":
                return await BrowserContext.Engine.InvokeAsync(() =>
                {
                    var properties = new JsonArray();
                    if (Value.IsObject)
                    {
                        var entries = Context.Call("(o) => { const result = []; for (const key of Object.keys(o)) result.push(key, o[key]); return result; }", Value).AsObject();
                        var length = (int)entries.GetProperty("length").AsNumber();
                        for (var i = 0; i + 1 < length; i += 2)
                        {
                            properties.Add((JsonNode)new JsonObject
                            {
                                ["name"] = entries.GetProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AsString(),
                                ["value"] = Frame.CreateHandle(Context, entries.GetProperty((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToReference(),
                            });
                        }
                    }

                    return new JsonObject { ["properties"] = properties };
                }).ConfigureAwait(false);

            case "jsonValue":
                return await BrowserContext.Engine.InvokeAsync(() => new JsonObject { ["value"] = Evaluation.SerializeValue(Context, Value) }).ConfigureAwait(false);

            default:
                return await InvokeElementAsync(method, parameters, progress).ConfigureAwait(false);
        }
    }

    protected virtual ValueTask<JsonNode?> InvokeElementAsync(string method, JsonElement parameters, Progress progress)
        => throw new PlaywrightError($"{Type}.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");

    protected static JsonElement GetArgument(JsonElement parameters) => parameters.TryGet("arg", out var argument) ? argument : default;
}
