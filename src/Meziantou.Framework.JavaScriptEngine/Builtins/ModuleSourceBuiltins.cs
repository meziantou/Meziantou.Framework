namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>%AbstractModuleSource% and its prototype (https://tc39.es/proposal-source-phase-imports/#sec-abstractmodulesource-objects).</summary>
internal static class ModuleSourceBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = new JsObject(intrinsics.ObjectPrototype);
        var constructor = realm.CreateBuiltinFunction("AbstractModuleSource", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineAccessor(prototype, WellKnownSymbols.ToStringTag, GetToStringTag, setter: null);
        return constructor;
    }

    /// <summary>%AbstractModuleSource%(): an abstract class</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThrowHelper.ThrowTypeError<JsValue>("AbstractModuleSource is an abstract class");
    }

    /// <summary>
    /// get %AbstractModuleSource%.prototype[@@toStringTag]: the [[ModuleSourceClassName]] of a module source, or undefined.
    /// The module sources are host objects (the source of a JavaScript module is not available).
    /// </summary>
    private static JsValue GetToStringTag(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return thisValue.Cell is JsHostObject { ModuleSourceClassName: { } className } ? JsValue.FromString(className) : JsValue.Undefined;
    }
}
