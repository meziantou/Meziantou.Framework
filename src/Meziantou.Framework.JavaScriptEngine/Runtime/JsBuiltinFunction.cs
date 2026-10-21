namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A built-in function object (https://tc39.es/ecma262/#sec-built-in-function-objects) implemented in .NET.</summary>
internal sealed class JsBuiltinFunction : JsFunction
{
    private readonly BuiltinFunctionCallback _callback;

    public JsBuiltinFunction(JsRealm realm, BuiltinFunctionCallback callback, bool isConstructor, JsObject? prototype)
        : base(realm, prototype)
    {
        _callback = callback;
        if (isConstructor)
        {
            Flags |= ObjectFlags.Constructor;
        }
    }

    /// <summary>Gets or sets a value associated with the function (e.g. the [[Promise]] of a resolving function).</summary>
    public object? State { get; set; }

    /// <summary>Gets or sets a second value associated with the function.</summary>
    public object? State2 { get; set; }

    /// <summary>Gets or sets whether the current realm while the function runs is the realm of its caller (see <see cref="JsRealm.CreateFunction(string, int, JsNativeFunction, bool)"/>).</summary>
    public bool RunsInCallerRealm { get; set; }

    internal override JsValue Call(JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        var engine = Realm.Engine;
        var previousRealm = engine.EnterNativeCall(Realm);
        if (RunsInCallerRealm && previousRealm is not null)
        {
            engine.CurrentRealm = previousRealm;
        }

        try
        {
            return _callback(this, thisValue, arguments, newTarget: null);
        }
        finally
        {
            engine.ExitNativeCall(previousRealm);
        }
    }

    internal override JsValue Construct(ReadOnlySpan<JsValue> arguments, JsObject newTarget)
    {
        var engine = Realm.Engine;
        var previousRealm = engine.EnterNativeCall(Realm);
        if (RunsInCallerRealm && previousRealm is not null)
        {
            engine.CurrentRealm = previousRealm;
        }

        try
        {
            return _callback(this, JsValue.Undefined, arguments, newTarget);
        }
        finally
        {
            engine.ExitNativeCall(previousRealm);
        }
    }
}
