using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The WeakMap constructor and WeakMap.prototype (https://tc39.es/ecma262/#sec-weakmap-objects).</summary>
internal static class WeakMapBuiltins
{
    private static readonly PropertyKey SetKey = new("set");

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("WeakMap", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "delete", 1, Delete);
        realm.DefineMethod(prototype, "get", 1, Get);
        realm.DefineMethod(prototype, "getOrInsert", 2, GetOrInsert);
        realm.DefineMethod(prototype, "getOrInsertComputed", 2, GetOrInsertComputed);
        realm.DefineMethod(prototype, "has", 1, Has);
        realm.DefineMethod(prototype, "set", 2, Set);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "WeakMap", PropertyFlags.Configurable);

        intrinsics.AddGlobal("WeakMap", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsWeakMap ThisWeakMap(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsWeakMap map)
            return map;

        return ThrowHelper.ThrowTypeError<JsWeakMap>($"Method WeakMap.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");
    }

    private static JsCell RequireWeakKey(JsValue key)
    {
        if (!WeakReferences.CanBeHeldWeakly(key))
            return ThrowHelper.ThrowTypeError<JsCell>($"Invalid value used as weak map key: {JsOperations.DescribeForError(key)}");

        return key.Cell!;
    }

    /// <summary>WeakMap([iterable])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor WeakMap requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.WeakMapPrototype);
        var map = new JsWeakMap(prototype);
        var iterable = GetArgument(arguments, 0);
        if (iterable.IsNullOrUndefined)
            return map;

        var adder = map.Get(SetKey);
        if (!adder.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("'set' returned for property 'set' of object '#<WeakMap>' is not a function");

        MapBuiltins.AddEntriesFromIterable(map, iterable, adder);
        return map;
    }

    /// <summary>WeakMap.prototype.delete(key)</summary>
    private static JsValue Delete(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisWeakMap(thisValue, "delete");
        var key = GetArgument(arguments, 0);
        return WeakReferences.CanBeHeldWeakly(key) && map.Data.Remove(key.Cell!);
    }

    /// <summary>WeakMap.prototype.get(key)</summary>
    private static JsValue Get(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisWeakMap(thisValue, "get");
        var key = GetArgument(arguments, 0);
        if (WeakReferences.CanBeHeldWeakly(key) && map.Data.TryGetValue(key.Cell!, out var box))
            return box.Value;

        return JsValue.Undefined;
    }

    /// <summary>WeakMap.prototype.getOrInsert(key, value)</summary>
    private static JsValue GetOrInsert(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisWeakMap(thisValue, "getOrInsert");
        var key = RequireWeakKey(GetArgument(arguments, 0));
        if (map.Data.TryGetValue(key, out var box))
            return box.Value;

        var value = GetArgument(arguments, 1);
        map.Data.Add(key, new StrongBox<JsValue>(value));
        return value;
    }

    /// <summary>WeakMap.prototype.getOrInsertComputed(key, callback)</summary>
    private static JsValue GetOrInsertComputed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisWeakMap(thisValue, "getOrInsertComputed");
        var callback = GetArgument(arguments, 1);
        if (!callback.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(callback)} is not a function");

        var keyValue = GetArgument(arguments, 0);
        var key = RequireWeakKey(keyValue);
        if (map.Data.TryGetValue(key, out var box))
            return box.Value;

        var value = callback.AsObject().Call(JsValue.Undefined, [keyValue]);

        // The callback may have modified the map: an entry it added is overwritten
        SetValue(map, key, value);
        return value;
    }

    /// <summary>WeakMap.prototype.has(key)</summary>
    private static JsValue Has(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisWeakMap(thisValue, "has");
        var key = GetArgument(arguments, 0);
        return WeakReferences.CanBeHeldWeakly(key) && map.Data.TryGetValue(key.Cell!, out _);
    }

    /// <summary>WeakMap.prototype.set(key, value)</summary>
    private static JsValue Set(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisWeakMap(thisValue, "set");
        var key = RequireWeakKey(GetArgument(arguments, 0));
        SetValue(map, key, GetArgument(arguments, 1));
        return map;
    }

    private static void SetValue(JsWeakMap map, JsCell key, JsValue value)
    {
        if (map.Data.TryGetValue(key, out var box))
        {
            box.Value = value;
        }
        else
        {
            map.Data.Add(key, new StrongBox<JsValue>(value));
        }
    }
}

/// <summary>The WeakSet constructor and WeakSet.prototype (https://tc39.es/ecma262/#sec-weakset-objects).</summary>
internal static class WeakSetBuiltins
{
    private static readonly PropertyKey AddKey = new("add");

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("WeakSet", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        realm.DefineMethod(prototype, "add", 1, Add);
        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "delete", 1, Delete);
        realm.DefineMethod(prototype, "has", 1, Has);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "WeakSet", PropertyFlags.Configurable);

        intrinsics.AddGlobal("WeakSet", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsWeakSet ThisWeakSet(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsWeakSet set)
            return set;

        return ThrowHelper.ThrowTypeError<JsWeakSet>($"Method WeakSet.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");
    }

    /// <summary>WeakSet([iterable])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor WeakSet requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.WeakSetPrototype);
        var set = new JsWeakSet(prototype);
        var iterable = GetArgument(arguments, 0);
        if (iterable.IsNullOrUndefined)
            return set;

        var adder = set.Get(AddKey);
        if (!adder.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("'add' returned for property 'add' of object '#<WeakSet>' is not a function");

        SetBuiltins.AddValuesFromIterable(set, iterable, adder.AsObject());
        return set;
    }

    /// <summary>WeakSet.prototype.add(value)</summary>
    private static JsValue Add(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisWeakSet(thisValue, "add");
        var value = GetArgument(arguments, 0);
        if (!WeakReferences.CanBeHeldWeakly(value))
            return ThrowHelper.ThrowTypeError<JsValue>($"Invalid value used in weak set: {JsOperations.DescribeForError(value)}");

        set.Data.TryAdd(value.Cell!, JsWeakSet.Present);
        return set;
    }

    /// <summary>WeakSet.prototype.delete(value)</summary>
    private static JsValue Delete(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisWeakSet(thisValue, "delete");
        var value = GetArgument(arguments, 0);
        return WeakReferences.CanBeHeldWeakly(value) && set.Data.Remove(value.Cell!);
    }

    /// <summary>WeakSet.prototype.has(value)</summary>
    private static JsValue Has(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisWeakSet(thisValue, "has");
        var value = GetArgument(arguments, 0);
        return WeakReferences.CanBeHeldWeakly(value) && set.Data.TryGetValue(value.Cell!, out _);
    }
}
