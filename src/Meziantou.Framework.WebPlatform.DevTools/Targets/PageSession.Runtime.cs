using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Runtime;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The Runtime domain
internal sealed partial class PageSession
{
    private const string GetPropertiesFunction = """
        (o, ownProperties, accessorPropertiesOnly) => {
          const result = [];
          const names = new Set();
          let current = o;
          let isOwn = true;
          while (current !== null && current !== undefined) {
            for (const key of Reflect.ownKeys(current)) {
              if (typeof key !== 'string' || names.has(key)) continue;
              const d = Object.getOwnPropertyDescriptor(current, key);
              if (!d) continue;
              const isAccessor = 'get' in d || 'set' in d;
              if (accessorPropertiesOnly && !isAccessor) continue;
              names.add(key);
              result.push({ name: key, hasValue: 'value' in d, value: d.value, get: d.get, set: d.set, enumerable: !!d.enumerable, configurable: !!d.configurable, writable: !!d.writable, isOwn });
            }
            if (ownProperties) break;
            current = Object.getPrototypeOf(current);
            isOwn = false;
          }
          return result;
        }
        """;

    private int _nextExceptionId;

    private async Task<JsonObject?> InvokeRuntimeAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "Runtime.enable":
                if (!RuntimeEnabled)
                {
                    RuntimeEnabled = true;
                    foreach (var frame in Target.GetFrames())
                    {
                        foreach (var context in frame.GetExecutionContexts())
                        {
                            SendEvent("Runtime.executionContextCreated", new JsonObject { ["context"] = context.ToDescription() });
                        }
                    }
                }

                return null;

            case "Runtime.disable":
                RuntimeEnabled = false;
                return null;

            case "Runtime.runIfWaitingForDebugger":
                Target.Resume();
                return null;

            case "Runtime.evaluate":
                return await EvaluateAsync(parameters).ConfigureAwait(true);

            case "Runtime.callFunctionOn":
                return await CallFunctionOnAsync(parameters).ConfigureAwait(true);

            case "Runtime.awaitPromise":
                var (promiseContext, promise) = Objects.Get(parameters.GetRequiredString("promiseObjectId"));
                return await CompleteEvaluationAsync(promiseContext, promise, awaitPromise: true, parameters.GetBoolean("returnByValue", defaultValue: false), group: null).ConfigureAwait(true);

            case "Runtime.getProperties":
                return GetProperties(parameters);

            case "Runtime.releaseObject":
                Objects.Release(parameters.GetRequiredString("objectId"));
                return null;

            case "Runtime.releaseObjectGroup":
                Objects.ReleaseGroup(parameters.GetRequiredString("objectGroup"));
                return null;

            case "Runtime.addBinding":
                AddBinding(parameters.GetRequiredString("name"));
                return null;

            case "Runtime.removeBinding":
                Bindings.Remove(parameters.GetRequiredString("name"));
                return null;

            case "Runtime.getIsolateId":
                return new JsonObject { ["id"] = Target.Context.Id };

            case "Runtime.getHeapUsage":
                return new JsonObject { ["usedSize"] = 0, ["totalSize"] = 0, ["embedderHeapUsedSize"] = 0, ["backingStorageSize"] = 0 };

            case "Runtime.globalLexicalScopeNames":
                return new JsonObject { ["names"] = new JsonArray() };

            case "Runtime.discardConsoleEntries":
            case "Runtime.setAsyncCallStackDepth":
            case "Runtime.setCustomObjectFormatterEnabled":
            case "Runtime.setMaxCallStackSizeToCapture":
            case "Runtime.terminateExecution":
                return null;

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    private ScriptContext GetExecutionContext(JsonElement parameters, string idName)
    {
        Target.EnsureLive();
        if (parameters.GetString("uniqueContextId") is { } uniqueId)
            return Target.FindExecutionContext(uniqueId) ?? throw new ProtocolException("Cannot find context with specified id");

        if (parameters.GetInt32(idName) is { } id)
            return Target.FindExecutionContext(id) ?? throw new ProtocolException("Cannot find context with specified id");

        return Target.MainFrame.MainWorld ?? throw new ProtocolException("Cannot find default execution context");
    }

    private async Task<JsonObject> EvaluateAsync(JsonElement parameters)
    {
        var context = GetExecutionContext(parameters, "contextId");
        var expression = parameters.GetRequiredString("expression");
        var returnByValue = parameters.GetBoolean("returnByValue", defaultValue: false);
        var awaitPromise = parameters.GetBoolean("awaitPromise", defaultValue: false);
        var group = parameters.GetString("objectGroup");
        JsValue value;
        try
        {
            value = context.Evaluate(expression);
        }
        catch (JsException ex)
        {
            return CreateExceptionResult(context, ex.Value);
        }

        return await CompleteEvaluationAsync(context, value, awaitPromise, returnByValue, group).ConfigureAwait(true);
    }

    private async Task<JsonObject> CallFunctionOnAsync(JsonElement parameters)
    {
        var declaration = parameters.GetRequiredString("functionDeclaration");
        ScriptContext context;
        JsValue thisValue;
        if (parameters.GetString("objectId") is { } objectId)
        {
            Target.EnsureLive();
            (context, thisValue) = Objects.Get(objectId);
        }
        else
        {
            if (parameters.GetInt32("executionContextId") is null && parameters.GetString("uniqueContextId") is null)
                throw new ProtocolException("Either ObjectId or executionContextId or uniqueContextId must be specified");

            context = GetExecutionContext(parameters, "executionContextId");
            thisValue = JsValue.Undefined;
        }

        var returnByValue = parameters.GetBoolean("returnByValue", defaultValue: false);
        var awaitPromise = parameters.GetBoolean("awaitPromise", defaultValue: false);
        var group = parameters.GetString("objectGroup");
        JsValue value;
        try
        {
            var arguments = new List<JsValue>();
            foreach (var argument in parameters.GetArray("arguments"))
            {
                arguments.Add(RemoteObjects.FromCallArgument(context, argument, Objects));
            }

            var function = context.GetFunction(declaration);
            if (!function.IsCallable)
                throw new ProtocolException("Given expression does not evaluate to a function");

            value = context.CallFunction(function, thisValue, [.. arguments]);
        }
        catch (JsException ex)
        {
            return CreateExceptionResult(context, ex.Value);
        }

        return await CompleteEvaluationAsync(context, value, awaitPromise, returnByValue, group).ConfigureAwait(true);
    }

    private async Task<JsonObject> CompleteEvaluationAsync(ScriptContext context, JsValue value, bool awaitPromise, bool returnByValue, string? group)
    {
        if (awaitPromise)
        {
            try
            {
                value = await context.AwaitAsync(value, Connection.Closed).ConfigureAwait(true);
            }
            catch (JsException ex)
            {
                return CreateExceptionResult(context, ex.Value);
            }
        }

        try
        {
            return new JsonObject { ["result"] = RemoteObjects.Create(context, value, Objects, group, returnByValue) };
        }
        catch (JsException ex)
        {
            return CreateExceptionResult(context, ex.Value);
        }
    }

    private JsonObject CreateExceptionResult(ScriptContext context, JsValue exception)
    {
        if (context.IsDestroyed)
            throw new ProtocolException("Execution context was destroyed.");

        var details = RemoteObjects.CreateExceptionDetails(context, exception, Objects, ++_nextExceptionId);
        return new JsonObject
        {
            ["result"] = details["exception"]!.DeepClone(),
            ["exceptionDetails"] = details,
        };
    }

    private JsonObject GetProperties(JsonElement parameters)
    {
        var objectId = parameters.GetRequiredString("objectId");
        var (context, value) = Objects.Get(objectId);

        // The objects of the properties are released with the inspected object (Runtime.releaseObjectGroup)
        var group = Objects.GetGroup(objectId);
        var result = new JsonArray();
        if (!value.IsObject)
            return new JsonObject { ["result"] = result };

        var ownProperties = parameters.GetBoolean("ownProperties", defaultValue: false);
        var accessorPropertiesOnly = parameters.GetBoolean("accessorPropertiesOnly", defaultValue: false);
        JsValue properties;
        try
        {
            properties = context.Call(GetPropertiesFunction, value, ownProperties, accessorPropertiesOnly);
        }
        catch (JsException ex)
        {
            return CreateExceptionResult(context, ex.Value);
        }

        var array = properties.AsObject();
        var length = (int)array.GetProperty("length").AsNumber();
        for (var i = 0; i < length; i++)
        {
            var property = array.GetProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AsObject();
            var descriptor = new JsonObject
            {
                ["name"] = property.GetProperty("name").AsString(),
                ["configurable"] = property.GetProperty("configurable").AsBoolean(),
                ["enumerable"] = property.GetProperty("enumerable").AsBoolean(),
                ["isOwn"] = property.GetProperty("isOwn").AsBoolean(),
            };
            if (property.GetProperty("hasValue").AsBoolean())
            {
                descriptor["value"] = RemoteObjects.Create(context, property.GetProperty("value"), Objects, group, returnByValue: false);
                descriptor["writable"] = property.GetProperty("writable").AsBoolean();
            }
            else
            {
                if (property.GetProperty("get") is { IsUndefined: false } getter)
                {
                    descriptor["get"] = RemoteObjects.Create(context, getter, Objects, group, returnByValue: false);
                }

                if (property.GetProperty("set") is { IsUndefined: false } setter)
                {
                    descriptor["set"] = RemoteObjects.Create(context, setter, Objects, group, returnByValue: false);
                }
            }

            result.Add((JsonNode)descriptor);
        }

        return new JsonObject { ["result"] = result, ["internalProperties"] = new JsonArray(), ["privateProperties"] = new JsonArray() };
    }

    private void AddBinding(string name)
    {
        if (!Bindings.Add(name))
            return;

        foreach (var frame in Target.GetFrames())
        {
            if (frame.MainWorld is { } context)
            {
                PageTarget.InstallBinding(context, this, name);
            }
        }
    }
}
