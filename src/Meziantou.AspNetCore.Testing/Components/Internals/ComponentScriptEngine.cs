using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>A script engine whose windows can call the JavaScript interop of .NET (<c>__mfBlazorHost</c>).</summary>
internal sealed class ComponentScriptEngine(JavaScriptScriptEngineOptions? options) : JavaScriptScriptEngine(options)
{
    public PageJSRuntime? Runtime { get; set; }

    protected override void OnRealmCreated(GlobalScope global, JsObject globalObject)
    {
        var realm = Engine.CurrentRealm!;
        var host = realm.CreateObject();
        host.SetProperty("endInvokeJS", realm.CreateFunction("endInvokeJS", 1, (_, arguments) =>
        {
            Runtime?.OnEndInvokeJS(arguments[0].AsString());
            return JsValue.Undefined;
        }));
        host.SetProperty("beginInvokeDotNet", realm.CreateFunction("beginInvokeDotNet", 5, (_, arguments) =>
        {
            Runtime?.OnBeginInvokeDotNet(
                callId: arguments[0].AsString(),
                assemblyName: arguments[1].IsString ? arguments[1].AsString() : null,
                methodIdentifier: arguments[2].AsString(),
                dotNetObjectId: (long)arguments[3].AsNumber(),
                argsJson: arguments[4].AsString());
            return JsValue.Undefined;
        }));
        globalObject.SetProperty("__mfBlazorHost", host);
    }
}
