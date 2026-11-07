namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Reflect object (https://tc39.es/ecma262/#sec-reflect-object).</summary>
internal static class ReflectBuiltins
{
    private static readonly BuiltinMethodTable ReflectMethods =
    [
        new("apply", 3, Apply),
        new("construct", 2, Construct),
        new("defineProperty", 3, DefineProperty),
        new("deleteProperty", 2, DeleteProperty),
        new("get", 2, Get),
        new("getOwnPropertyDescriptor", 2, GetOwnPropertyDescriptor),
        new("getPrototypeOf", 1, GetPrototypeOf),
        new("has", 2, Has),
        new("isExtensible", 1, IsExtensible),
        new("ownKeys", 1, OwnKeys),
        new("preventExtensions", 1, PreventExtensions),
        new("set", 3, Set),
        new("setPrototypeOf", 2, SetPrototypeOf),
    ];

    public static JsObject Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var reflect = new JsObject(intrinsics.ObjectPrototype);
        realm.DefineMethods(reflect, ReflectMethods);
        reflect.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Reflect", PropertyFlags.Configurable);
        intrinsics.AddGlobal("Reflect", reflect);
        return reflect;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsObject RequireObject(JsValue value, string methodName)
    {
        if (value.Cell is JsObject obj)
            return obj;

        return ThrowHelper.ThrowTypeError<JsObject>($"Reflect.{methodName} called on non-object");
    }

    /// <summary>Reflect.apply(target, thisArgument, argumentsList)</summary>
    private static JsValue Apply(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (Arg(arguments, 0).Cell is not JsObject { IsCallable: true } target)
            return ThrowHelper.ThrowTypeError<JsValue>("Reflect.apply target is not a function");

        var args = PropertyOperations.CreateListFromArrayLike(Arg(arguments, 2));
        return target.Call(Arg(arguments, 1), args);
    }

    /// <summary>Reflect.construct(target, argumentsList [, newTarget])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (Arg(arguments, 0).Cell is not JsObject { IsConstructor: true } target)
            return ThrowHelper.ThrowTypeError<JsValue>("Reflect.construct target is not a constructor");

        var constructNewTarget = target;
        if (arguments.Length > 2)
        {
            if (arguments[2].Cell is not JsObject { IsConstructor: true } newTargetObject)
                return ThrowHelper.ThrowTypeError<JsValue>("Reflect.construct newTarget is not a constructor");

            constructNewTarget = newTargetObject;
        }

        var args = PropertyOperations.CreateListFromArrayLike(Arg(arguments, 1));
        return target.Construct(args, constructNewTarget);
    }

    /// <summary>Reflect.defineProperty(target, propertyKey, attributes)</summary>
    private static JsValue DefineProperty(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "defineProperty");
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        var descriptor = PropertyOperations.ToPropertyDescriptor(Arg(arguments, 2));
        return target.DefineOwnProperty(key, descriptor);
    }

    /// <summary>Reflect.deleteProperty(target, propertyKey)</summary>
    private static JsValue DeleteProperty(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "deleteProperty");
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        return target.Delete(key);
    }

    /// <summary>Reflect.get(target, propertyKey [, receiver])</summary>
    private static JsValue Get(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "get");
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        var receiver = arguments.Length > 2 ? arguments[2] : new JsValue(target);
        return target.Get(key, receiver);
    }

    /// <summary>Reflect.getOwnPropertyDescriptor(target, propertyKey)</summary>
    private static JsValue GetOwnPropertyDescriptor(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "getOwnPropertyDescriptor");
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        var found = target.GetOwnProperty(key, out var descriptor);
        return PropertyOperations.FromPropertyDescriptor(function.Realm, found, descriptor);
    }

    /// <summary>Reflect.getPrototypeOf(target)</summary>
    private static JsValue GetPrototypeOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "getPrototypeOf");
        return JsValue.FromObject(target.GetPrototypeOf());
    }

    /// <summary>Reflect.has(target, propertyKey)</summary>
    private static JsValue Has(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "has");
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        return target.HasProperty(key);
    }

    /// <summary>Reflect.isExtensible(target)</summary>
    private static JsValue IsExtensible(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "isExtensible");
        return target.IsExtensible();
    }

    /// <summary>Reflect.ownKeys(target)</summary>
    private static JsValue OwnKeys(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "ownKeys");
        var keys = target.OwnPropertyKeys();
        var array = new JsArray(function.Realm.Intrinsics.ArrayPrototype);
        foreach (var key in keys)
        {
            array.Push(key.ToJsValue());
        }

        return array;
    }

    /// <summary>Reflect.preventExtensions(target)</summary>
    private static JsValue PreventExtensions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "preventExtensions");
        return target.PreventExtensions();
    }

    /// <summary>Reflect.set(target, propertyKey, V [, receiver])</summary>
    private static JsValue Set(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "set");
        var key = JsOperations.ToPropertyKey(Arg(arguments, 1));
        var receiver = arguments.Length > 3 ? arguments[3] : new JsValue(target);
        return target.Set(key, Arg(arguments, 2), receiver);
    }

    /// <summary>Reflect.setPrototypeOf(target, proto)</summary>
    private static JsValue SetPrototypeOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = RequireObject(Arg(arguments, 0), "setPrototypeOf");
        var proto = Arg(arguments, 1);
        if (!proto.IsObject && !proto.IsNull)
            return ThrowHelper.ThrowTypeError<JsValue>($"Object prototype may only be an Object or null: {JsOperations.DescribeForError(proto)}");

        return target.SetPrototypeOf(proto.TryGetObject());
    }
}
