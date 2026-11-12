using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Runtime;

/// <summary>The objects given to the client of a session (their <c>objectId</c>), until it releases them or their context is destroyed.</summary>
internal sealed class RemoteObjectRegistry
{
    private readonly Dictionary<string, (ScriptContext Context, JsValue Value, string? Group)> _objects = new(StringComparer.Ordinal);
    private long _nextId;

    public string Add(ScriptContext context, JsValue value, string? group)
    {
        var id = context.Id.ToString(CultureInfo.InvariantCulture) + "." + (++_nextId).ToString(CultureInfo.InvariantCulture);
        _objects.Add(id, (context, value, group));
        return id;
    }

    public bool TryGet(string objectId, out ScriptContext context, out JsValue value)
    {
        if (_objects.TryGetValue(objectId, out var entry) && !entry.Context.IsDestroyed)
        {
            context = entry.Context;
            value = entry.Value;
            return true;
        }

        context = null!;
        value = default;
        return false;
    }

    public (ScriptContext Context, JsValue Value) Get(string objectId)
    {
        if (!TryGet(objectId, out var context, out var value))
            throw new ProtocolException("Could not find object with given id");

        return (context, value);
    }

    public void Release(string objectId) => _objects.Remove(objectId);

    public void ReleaseGroup(string group)
    {
        foreach (var (id, entry) in _objects.ToArray())
        {
            if (entry.Group == group)
            {
                _objects.Remove(id);
            }
        }
    }

    public void RemoveContext(ScriptContext context)
    {
        foreach (var (id, entry) in _objects.ToArray())
        {
            if (ReferenceEquals(entry.Context, context))
            {
                _objects.Remove(id);
            }
        }
    }

    public void Clear() => _objects.Clear();
}

/// <summary>The conversions between the values of the scripts and the <c>RemoteObject</c> of the protocol.</summary>
internal static class RemoteObjects
{
    // [type, subtype, className, description] of an object (the description of the errors is their stack)
    private const string DescribeFunction = """
        (v) => {
          const tag = Object.prototype.toString.call(v).slice(8, -1);
          let className = tag;
          try { const c = v.constructor; if (typeof c === 'function' && typeof c.name === 'string' && c.name) className = c.name; } catch {}
          if (typeof v === 'function') { let d = ''; try { d = Function.prototype.toString.call(v); } catch {} return ['function', '', 'Function', d]; }
          if (Array.isArray(v)) return ['object', 'array', className, className + '(' + v.length + ')'];
          if (v instanceof Error || tag === 'Error') {
            let head = className; try { head = String(v); } catch {}
            let stack = ''; try { if (typeof v.stack === 'string') stack = v.stack; } catch {}
            return ['object', 'error', className, stack.startsWith(head) ? stack : (stack ? head + '\n' + stack : head)];
          }
          const size = (o) => { try { return o.size; } catch { return 0; } };
          switch (tag) {
            case 'RegExp': return ['object', 'regexp', className, String(v)];
            case 'Date': { let d = ''; try { d = Date.prototype.toString.call(v); } catch {} return ['object', 'date', className, d]; }
            case 'Map': return ['object', 'map', className, className + '(' + size(v) + ')'];
            case 'Set': return ['object', 'set', className, className + '(' + size(v) + ')'];
            case 'WeakMap': return ['object', 'weakmap', className, className];
            case 'WeakSet': return ['object', 'weakset', className, className];
            case 'WeakRef': return ['object', 'weakref', className, className];
            case 'Promise': return ['object', 'promise', className, 'Promise'];
            case 'ArrayBuffer': case 'SharedArrayBuffer': { let n = 0; try { n = v.byteLength; } catch {} return ['object', 'arraybuffer', className, className + '(' + n + ')']; }
            case 'DataView': return ['object', 'dataview', className, className];
            case 'Generator': case 'AsyncGenerator': return ['object', 'generator', className, className];
          }
          if (ArrayBuffer.isView(v)) { let n = 0; try { n = v.length; } catch {} return ['object', 'typedarray', className, className + '(' + n + ')']; }
          if (tag.endsWith('Iterator')) return ['object', 'iterator', className, className];
          return ['object', '', className, className];
        }
        """;

    /// <summary>Creates the <c>RemoteObject</c> of a value: by value (<c>returnByValue</c>), or as an object of the registry.</summary>
    public static JsonObject Create(ScriptContext context, JsValue value, RemoteObjectRegistry registry, string? group, bool returnByValue)
    {
        if (value.IsUndefined)
            return new JsonObject { ["type"] = "undefined" };

        if (value.IsNull)
            return new JsonObject { ["type"] = "object", ["subtype"] = "null", ["value"] = null };

        if (value.IsBoolean)
            return new JsonObject { ["type"] = "boolean", ["value"] = value.AsBoolean() };

        if (value.IsString)
            return new JsonObject { ["type"] = "string", ["value"] = value.AsString() };

        if (value.IsNumber)
            return CreateNumber(value.AsNumber());

        if (value.IsBigInt)
        {
            var text = value.AsBigInt().ToString(CultureInfo.InvariantCulture) + "n";
            return new JsonObject { ["type"] = "bigint", ["unserializableValue"] = text, ["description"] = text };
        }

        if (value.IsSymbol)
        {
            var description = context.Call("(s) => String(s)", value);
            var symbol = new JsonObject { ["type"] = "symbol", ["description"] = description.IsString ? description.AsString() : "Symbol()" };
            if (!returnByValue)
            {
                symbol["objectId"] = registry.Add(context, value, group);
            }

            return symbol;
        }

        if (returnByValue)
        {
            var json = context.Stringify(value);
            if (json is null)
            {
                // A function is serialized as an empty object, as Chromium does
                return value.IsCallable ? new JsonObject { ["type"] = "function", ["value"] = new JsonObject() } : new JsonObject { ["type"] = "undefined" };
            }

            var type = value.IsCallable ? "function" : "object";
            return new JsonObject { ["type"] = type, ["value"] = JsonNode.Parse(json) };
        }

        var result = new JsonObject();
        if (context.FromJsValue(value) is Node node)
        {
            result["type"] = "object";
            result["subtype"] = "node";
            result["className"] = GetClassName(context, value);
            result["description"] = DescribeNode(node);
        }
        else
        {
            var details = context.Call(DescribeFunction, value).AsObject();
            var type = details.GetProperty("0").AsString();
            var subtype = details.GetProperty("1").AsString();
            result["type"] = type;
            if (subtype.Length > 0)
            {
                result["subtype"] = subtype;
            }

            result["className"] = details.GetProperty("2").AsString();
            result["description"] = details.GetProperty("3").AsString();
        }

        result["objectId"] = registry.Add(context, value, group);
        return result;
    }

    private static string GetClassName(ScriptContext context, JsValue value)
    {
        var name = context.Call("(v) => { try { return v.constructor.name; } catch { return ''; } }", value);
        return name.IsString && name.AsString().Length > 0 ? name.AsString() : "Object";
    }

    public static JsonObject CreateNumber(double number)
    {
        var description = FormatNumber(number);
        if (double.IsNaN(number) || double.IsInfinity(number) || (number == 0 && double.IsNegative(number)))
            return new JsonObject { ["type"] = "number", ["unserializableValue"] = description, ["description"] = description };

        return new JsonObject { ["type"] = "number", ["value"] = number, ["description"] = description };
    }

    private static string FormatNumber(double number)
    {
        if (double.IsNaN(number))
            return "NaN";

        if (double.IsPositiveInfinity(number))
            return "Infinity";

        if (double.IsNegativeInfinity(number))
            return "-Infinity";

        if (number == 0)
            return double.IsNegative(number) ? "-0" : "0";

        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>The description of a node, as the developer tools show it (e.g. <c>div#main.header</c>).</summary>
    public static string DescribeNode(Node node)
    {
        if (node is not Element element)
            return node.NodeName;

        var builder = new StringBuilder(element.LocalName);
        if (element.GetAttribute("id") is { Length: > 0 } id)
        {
            builder.Append('#').Append(id);
        }

        if (element.GetAttribute("class") is { Length: > 0 } classes)
        {
            foreach (var className in classes.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Append('.').Append(className);
            }
        }

        return builder.ToString();
    }

    /// <summary>Converts an argument of <c>Runtime.callFunctionOn</c> (<c>{ value }</c>, <c>{ unserializableValue }</c>, <c>{ objectId }</c>, or <c>{}</c> for undefined).</summary>
    public static JsValue FromCallArgument(ScriptContext context, JsonElement argument, RemoteObjectRegistry registry)
    {
        if (argument.GetString("objectId") is { } objectId)
        {
            var (objectContext, value) = registry.Get(objectId);
            if (!ReferenceEquals(objectContext.Document, context.Document))
                throw new ProtocolException("Argument should belong to the same JavaScript world as target object");

            return value;
        }

        if (argument.GetString("unserializableValue") is { } unserializable)
            return ParseUnserializableValue(unserializable);

        if (argument.ValueKind == JsonValueKind.Object && argument.TryGetProperty("value", out var json))
        {
            return json.ValueKind switch
            {
                JsonValueKind.Null => JsValue.Null,
                JsonValueKind.True => JsValue.FromBoolean(true),
                JsonValueKind.False => JsValue.FromBoolean(false),
                JsonValueKind.Number => JsValue.FromNumber(json.GetDouble()),
                JsonValueKind.String => JsValue.FromString(json.GetString()!),
                _ => context.ParseJson(json.GetRawText()),
            };
        }

        return JsValue.Undefined;
    }

    private static JsValue ParseUnserializableValue(string value)
    {
        switch (value)
        {
            case "NaN":
                return JsValue.FromNumber(double.NaN);
            case "Infinity":
                return JsValue.FromNumber(double.PositiveInfinity);
            case "-Infinity":
                return JsValue.FromNumber(double.NegativeInfinity);
            case "-0":
                return JsValue.FromNumber(-0d);
        }

        if (value.EndsWith('n', StringComparison.Ordinal) && System.Numerics.BigInteger.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bigint))
            return JsValue.FromBigInt(bigint);

        throw new ProtocolException("Couldn't parse value object in call argument");
    }

    /// <summary>The <c>exceptionDetails</c> of a value thrown by a script.</summary>
    public static JsonObject CreateExceptionDetails(ScriptContext context, JsValue exception, RemoteObjectRegistry? registry, int exceptionId)
    {
        JsonObject remoteException;
        try
        {
            remoteException = registry is not null
                ? Create(context, exception, registry, group: null, returnByValue: false)
                : DescribeWithoutRegistry(context, exception);
        }
        catch (Exception ex) when (ex is JsException or ProtocolException)
        {
            remoteException = new JsonObject { ["type"] = "object", ["subtype"] = "error", ["className"] = "Error", ["description"] = "Error" };
        }

        var description = remoteException["description"]?.GetValue<string>() ?? (exception.IsString ? exception.AsString() : "");
        var (url, line, column) = GetLocation(description);
        var details = new JsonObject
        {
            ["exceptionId"] = exceptionId,
            ["text"] = exception.IsObject ? "Uncaught" : "Uncaught " + description,
            ["lineNumber"] = line,
            ["columnNumber"] = column,
            ["exception"] = remoteException,
            ["executionContextId"] = context.Id,
        };
        if (url is not null)
        {
            details["url"] = url;
        }

        return details;
    }

    private static JsonObject DescribeWithoutRegistry(ScriptContext context, JsValue value)
    {
        var registry = new RemoteObjectRegistry();
        var result = Create(context, value, registry, group: null, returnByValue: false);
        result.Remove("objectId");
        return result;
    }

    // The location of the first frame of a stack ("    at name (url:line:column)" or "    at url:line:column"), 0-based
    private static (string? Url, int Line, int Column) GetLocation(string stack)
    {
        var index = stack.IndexOf("\n    at ", StringComparison.Ordinal);
        if (index < 0)
            return (null, 0, 0);

        var end = stack.IndexOf('\n', index + 1, StringComparison.Ordinal);
        var frame = (end < 0 ? stack[(index + 8)..] : stack[(index + 8)..end]).Trim();
        if (frame.EndsWith(')', StringComparison.Ordinal) && frame.LastIndexOf('(', StringComparison.Ordinal) is var open and >= 0)
        {
            frame = frame[(open + 1)..^1];
        }

        var lastColon = frame.LastIndexOf(':', StringComparison.Ordinal);
        if (lastColon <= 0)
            return (null, 0, 0);

        var lineColon = frame.LastIndexOf(':', lastColon - 1, StringComparison.Ordinal);
        if (lineColon <= 0)
            return (null, 0, 0);

        if (!int.TryParse(frame.AsSpan(lineColon + 1, lastColon - lineColon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var line)
            || !int.TryParse(frame.AsSpan(lastColon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var column))
            return (null, 0, 0);

        return (frame[..lineColon], Math.Max(0, line - 1), Math.Max(0, column - 1));
    }
}
