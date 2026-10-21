namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Boolean constructor and Boolean.prototype (https://tc39.es/ecma262/#sec-boolean-objects).</summary>
internal static class BooleanBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.BooleanPrototype;
        var constructor = realm.CreateBuiltinFunction("Boolean", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "toString", 0, ToString);
        realm.DefineMethod(prototype, "valueOf", 0, ValueOf);
        intrinsics.AddGlobal("Boolean", constructor);
        return constructor;
    }

    /// <summary>Boolean(value)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var b = JsOperations.ToBoolean(arguments.Length > 0 ? arguments[0] : default);
        if (newTarget is null)
            return b;

        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, static i => i.BooleanPrototype);
        return new JsPrimitiveObject(prototype, ObjectClass.Boolean, b);
    }

    /// <summary>ThisBooleanValue(value)</summary>
    private static bool ThisBooleanValue(JsValue value, string methodName)
    {
        if (value.Cell is BooleanCell boolean)
            return boolean.Value;

        if (value.Cell is JsPrimitiveObject { Class: ObjectClass.Boolean } obj)
            return obj.PrimitiveValue.AsBoolean();

        return ThrowHelper.ThrowTypeError<bool>($"Boolean.prototype.{methodName} requires that 'this' be a Boolean");
    }

    /// <summary>Boolean.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisBooleanValue(thisValue, "toString") ? "true" : "false";
    }

    /// <summary>Boolean.prototype.valueOf()</summary>
    private static JsValue ValueOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisBooleanValue(thisValue, "valueOf");
    }
}
