using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// Calls the JavaScript functions of the page, as <c>Microsoft.JSInterop.js</c> does in a browser: the arguments and the
/// results are JSON, the element references are the elements whose <c>_bl_</c> attribute matches, and the .NET object
/// references can be called back with <c>invokeMethodAsync()</c>.
/// </summary>
internal sealed class PageJSRuntime : JSRuntime
{
    private const string HelperScript = """
        globalThis.__mfBlazor ??= (() => {
          const host = globalThis.__mfBlazorHost;
          const objects = new Map([[0, globalThis]]);
          let nextObjectId = 1;
          const pendingDotNetCalls = new Map();
          let nextCallId = 1;
          const createDotNetObject = id => ({
            invokeMethodAsync: (method, ...args) => beginInvokeDotNet(null, method, id, args),
            invokeMethod: () => { throw new Error('Synchronous calls to .NET are not supported'); },
            dispose: () => {},
            serializeAsArg: () => ({ __dotNetObject: id }),
          });
          const revive = (key, value) => {
            if (value && typeof value === 'object') {
              if (typeof value.__internalId === 'string') return document.querySelector('[_bl_' + value.__internalId + ']');
              if (typeof value.__jsObjectId === 'number') return objects.get(value.__jsObjectId);
              if (typeof value.__dotNetObject === 'number') return createDotNetObject(value.__dotNetObject);
            }
            return value;
          };
          const replace = (key, value) => value && typeof value === 'object' && typeof value.serializeAsArg === 'function' ? value.serializeAsArg() : value;
          const beginInvokeDotNet = (assembly, method, objectId, args) => new Promise((resolve, reject) => {
            const callId = String(nextCallId++);
            pendingDotNetCalls.set(callId, { resolve, reject });
            host.beginInvokeDotNet(callId, assembly, method, objectId, JSON.stringify(args, replace));
          });
          const formatError = error => error instanceof Error ? error.message + '\n' + error.stack : String(error);
          const resolveMember = (identifier, target) => {
            let owner = target;
            let value = target;
            for (const part of identifier.split('.')) {
              owner = value;
              value = value?.[part];
              if (value === undefined) throw new Error("Could not find '" + identifier + "' ('" + part + "' was undefined).");
            }
            return { owner, value };
          };
          const convertResult = (value, resultType) => {
            if (resultType === 'JSObjectReference') {
              const id = nextObjectId++;
              objects.set(id, value);
              return { __jsObjectId: id };
            }
            return resultType === 'JSVoidResult' ? null : value;
          };
          globalThis.DotNet ??= {
            invokeMethodAsync: (assembly, method, ...args) => beginInvokeDotNet(assembly, method, 0, args),
            disposeJSObjectReferenceById: id => objects.delete(id),
          };
          return {
            invoke(taskId, identifier, argsJson, resultType, targetInstanceId, callType) {
              let result;
              try {
                const args = argsJson ? JSON.parse(argsJson, revive) : [];
                const target = objects.get(targetInstanceId);
                if (identifier === 'import' && targetInstanceId === 0) {
                  result = import(new URL(args[0], document.baseURI).href);
                } else if (callType === 'ConstructorCall') {
                  const { value } = resolveMember(identifier, target);
                  result = new value(...args);
                } else if (callType === 'GetValue') {
                  result = resolveMember(identifier, target).value;
                } else if (callType === 'SetValue') {
                  const parts = identifier.split('.');
                  const last = parts.pop();
                  const owner = parts.length ? resolveMember(parts.join('.'), target).value : target;
                  owner[last] = args[0];
                } else {
                  const { owner, value } = resolveMember(identifier, target);
                  if (typeof value !== 'function') throw new Error("The value '" + identifier + "' is not a function.");
                  result = value.apply(owner, args);
                }
              } catch (error) {
                host.endInvokeJS(JSON.stringify([taskId, false, formatError(error)]));
                return;
              }
              Promise.resolve(result).then(
                value => {
                  let json;
                  try { json = JSON.stringify([taskId, true, convertResult(value, resultType)], replace); }
                  catch (error) { json = JSON.stringify([taskId, false, formatError(error)]); }
                  host.endInvokeJS(json);
                },
                error => host.endInvokeJS(JSON.stringify([taskId, false, formatError(error)])));
            },
            endInvokeDotNet(callId, success, resultOrError) {
              const call = pendingDotNetCalls.get(callId);
              if (!call) return;
              pendingDotNetCalls.delete(callId);
              if (success) call.resolve(resultOrError ? JSON.parse(resultOrError, revive) : null);
              else call.reject(new Error(resultOrError));
            },
          };
        })();
        """;

    private readonly ComponentScriptEngine _engine;
    private readonly Func<BrowsingContext> _browsingContext;
    private readonly EventLoopDispatcher _dispatcher;

    public PageJSRuntime(ComponentScriptEngine engine, Func<BrowsingContext> browsingContext, EventLoopDispatcher dispatcher)
    {
        _engine = engine;
        _browsingContext = browsingContext;
        _dispatcher = dispatcher;
        JsonSerializerOptions.Converters.Add(new ElementReferenceJsonConverter());
        engine.Runtime = this;
    }

    protected override void BeginInvokeJS(long taskId, string identifier, string? argsJson, JSCallResultType resultType, long targetInstanceId)
        => BeginInvoke(taskId, identifier, argsJson, resultType, targetInstanceId, JSCallType.FunctionCall);

    protected override void BeginInvokeJS(in JSInvocationInfo invocationInfo)
        => BeginInvoke(invocationInfo.AsyncHandle, invocationInfo.Identifier, invocationInfo.ArgsJson, invocationInfo.ResultType, invocationInfo.TargetInstanceId, invocationInfo.CallType);

    private void BeginInvoke(long taskId, string identifier, string? argsJson, JSCallResultType resultType, long targetInstanceId, JSCallType callType)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            var window = _browsingContext().Window;
            var invoke = _engine.Evaluate(window, HelperScript + "globalThis.__mfBlazor.invoke.bind(globalThis.__mfBlazor)");
            _engine.Call(window, invoke, JsValue.Undefined,
                (double)taskId,
                identifier,
                argsJson is null ? JsValue.Null : (JsValue)argsJson,
                resultType.ToString(),
                (double)targetInstanceId,
                callType.ToString());
        });
    }

    protected override void EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult)
    {
        var callId = invocationInfo.CallId;
        if (callId is null)
            return;

        var success = invocationResult.Success;
        var payload = success ? invocationResult.ResultJson : invocationResult.Exception?.ToString();
        _ = _dispatcher.InvokeAsync(() =>
        {
            var window = _browsingContext().Window;
            var endInvoke = _engine.Evaluate(window, HelperScript + "globalThis.__mfBlazor.endInvokeDotNet.bind(globalThis.__mfBlazor)");
            _engine.Call(window, endInvoke, JsValue.Undefined, callId, success, payload is null ? JsValue.Null : (JsValue)payload);
        });
    }

    internal void OnEndInvokeJS(string json) => DotNetDispatcher.EndInvokeJS(this, json);

    internal void OnBeginInvokeDotNet(string callId, string? assemblyName, string methodIdentifier, long dotNetObjectId, string argsJson)
        => DotNetDispatcher.BeginInvokeDotNet(this, new DotNetInvocationInfo(assemblyName, methodIdentifier, dotNetObjectId, callId), argsJson);

    private sealed class ElementReferenceJsonConverter : JsonConverter<ElementReference>
    {
        public override ElementReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? id = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("__internalId"u8))
                {
                    reader.Read();
                    id = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            return new ElementReference(id ?? throw new JsonException("The element reference has no __internalId"));
        }

        public override void Write(Utf8JsonWriter writer, ElementReference value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("__internalId", value.Id);
            writer.WriteEndObject();
        }
    }
}
