namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Proxy constructor (https://tc39.es/ecma262/#sec-proxy-objects).</summary>
internal static class ProxyBuiltins
{
    private static readonly PropertyKey ProxyKey = new("proxy");
    private static readonly PropertyKey RevokeKey = new("revoke");

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;

        // The Proxy constructor has no "prototype" property
        var constructor = realm.CreateBuiltinFunction("Proxy", 2, Construct, isConstructor: true);
        realm.DefineMethod(constructor, "revocable", 2, Revocable);
        intrinsics.AddGlobal("Proxy", constructor);
        return constructor;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>Proxy(target, handler)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Proxy requires 'new'");

        return JsProxy.Create(function.Realm.Engine, Arg(arguments, 0), Arg(arguments, 1));
    }

    /// <summary>Proxy.revocable(target, handler)</summary>
    private static JsValue Revocable(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var realm = function.Realm;
        var proxy = JsProxy.Create(realm.Engine, Arg(arguments, 0), Arg(arguments, 1));
        var revoker = realm.CreateBuiltinFunction("", 0, Revoke);
        revoker.State = proxy;
        var result = new JsObject(realm.Intrinsics.ObjectPrototype);
        result.CreateDataPropertyOrThrow(ProxyKey, proxy);
        result.CreateDataPropertyOrThrow(RevokeKey, revoker);
        return result;
    }

    /// <summary>The revocation function of Proxy.revocable.</summary>
    private static JsValue Revoke(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (function.State is JsProxy proxy)
        {
            function.State = null;
            proxy.Revoke();
        }

        return JsValue.Undefined;
    }
}
