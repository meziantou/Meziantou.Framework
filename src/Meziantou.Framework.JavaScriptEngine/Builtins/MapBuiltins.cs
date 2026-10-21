namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Map constructor, Map.prototype and %MapIteratorPrototype% (https://tc39.es/ecma262/#sec-map-objects).</summary>
internal static class MapBuiltins
{
    private const double MaxSafeInteger = 9007199254740991;

    private static readonly PropertyKey SetKey = new("set");
    private static readonly PropertyKey ZeroKey = new(0);
    private static readonly PropertyKey OneKey = new(1);

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype, JsObject iteratorPrototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("Map", 0, Construct, isConstructor: true);
        realm.DefineMethod(constructor, "groupBy", 2, GroupBy);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, static (_, thisValue, _, _) => thisValue, setter: null);

        realm.DefineMethod(prototype, "clear", 0, Clear);
        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "delete", 1, Delete);
        var entries = realm.DefineMethod(prototype, "entries", 0, static (_, thisValue, _, _) => CreateMapIterator(thisValue, CollectionIteratorKind.Entries));
        realm.DefineMethod(prototype, "forEach", 1, ForEach);
        realm.DefineMethod(prototype, "get", 1, Get);
        realm.DefineMethod(prototype, "getOrInsert", 2, GetOrInsert);
        realm.DefineMethod(prototype, "getOrInsertComputed", 2, GetOrInsertComputed);
        realm.DefineMethod(prototype, "has", 1, Has);
        realm.DefineMethod(prototype, "keys", 0, static (_, thisValue, _, _) => CreateMapIterator(thisValue, CollectionIteratorKind.Keys));
        realm.DefineMethod(prototype, "set", 2, Set);
        realm.DefineAccessor(prototype, "size", GetSize, setter: null);
        realm.DefineMethod(prototype, "values", 0, static (_, thisValue, _, _) => CreateMapIterator(thisValue, CollectionIteratorKind.Values));
        prototype.DefineBuiltIn(WellKnownSymbols.Iterator, entries);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Map", PropertyFlags.Configurable);

        realm.DefineMethod(iteratorPrototype, "next", 0, static (_, thisValue, _, _) => CollectionIteratorNext(thisValue, isMapIterator: true));
        iteratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Map Iterator", PropertyFlags.Configurable);

        intrinsics.AddGlobal("Map", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsMap ThisMap(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsMap map)
            return map;

        return ThrowHelper.ThrowTypeError<JsMap>($"Method Map.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");
    }

    /// <summary>Map([iterable])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Map requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.MapPrototype);
        var map = new JsMap(prototype);
        var iterable = GetArgument(arguments, 0);
        if (iterable.IsNullOrUndefined)
            return map;

        var adder = map.Get(SetKey);
        if (!adder.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("'set' returned for property 'set' of object '#<Map>' is not a function");

        AddEntriesFromIterable(map, iterable, adder);
        return map;
    }

    /// <summary>AddEntriesFromIterable(target, iterable, adder)</summary>
    internal static void AddEntriesFromIterable(JsObject target, JsValue iterable, JsValue adder)
    {
        var iteratorRecord = IteratorOperations.GetIterator(iterable);
        var adderFunction = adder.AsObject();
        while (IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
        {
            try
            {
                if (next.Cell is not JsObject entry)
                {
                    ThrowHelper.ThrowTypeError($"Iterator value {JsOperations.DescribeForError(next)} is not an entry object");
                    return;
                }

                var key = entry.Get(ZeroKey);
                var value = entry.Get(OneKey);
                adderFunction.Call(target, [key, value]);
            }
            catch (JsException)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw;
            }
        }
    }

    /// <summary>Map.groupBy(items, callback)</summary>
    private static JsValue GroupBy(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var items = GetArgument(arguments, 0);
        var callback = GetArgument(arguments, 1);

        // GroupBy(items, callback, collection)
        JsOperations.RequireObjectCoercible(items);
        if (!callback.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(callback)} is not a function");

        var callbackFunction = callback.AsObject();
        var groupIndices = new OrderedHashTable(hasValues: true);
        var groups = new List<(JsValue Key, List<JsValue> Elements)>();
        var iteratorRecord = IteratorOperations.GetIterator(items);
        double k = 0;
        while (true)
        {
            if (k >= MaxSafeInteger)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                return ThrowHelper.ThrowTypeError<JsValue>("Too many elements");
            }

            if (!IteratorOperations.IteratorStepValue(iteratorRecord, out var value))
                break;

            JsValue key;
            try
            {
                key = callbackFunction.Call(JsValue.Undefined, [value, k]);
            }
            catch (JsException)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw;
            }

            // AddValueToKeyedGroup(groups, key, value)
            key = OrderedHashTable.CanonicalizeKey(key);
            if (groupIndices.TryGetValue(key, out var index))
            {
                groups[(int)index.NumberValue].Elements.Add(value);
            }
            else
            {
                groupIndices.Set(key, groups.Count);
                groups.Add((key, [value]));
            }

            k++;
        }

        var realm = function.Realm;
        var map = new JsMap(realm.Intrinsics.MapPrototype);
        foreach (var (key, elements) in groups)
        {
            map.Data.Set(key, JsArray.Create(realm, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(elements)));
        }

        return map;
    }

    /// <summary>Map.prototype.clear()</summary>
    private static JsValue Clear(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        ThisMap(thisValue, "clear").Data.Clear();
        return JsValue.Undefined;
    }

    /// <summary>Map.prototype.delete(key)</summary>
    private static JsValue Delete(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisMap(thisValue, "delete").Data.Remove(GetArgument(arguments, 0));
    }

    /// <summary>Map.prototype.forEach(callback [, thisArg])</summary>
    private static JsValue ForEach(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisMap(thisValue, "forEach");
        var callback = GetArgument(arguments, 0);
        if (!callback.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(callback)} is not a function");

        var callbackFunction = callback.AsObject();
        var thisArg = GetArgument(arguments, 1);
        var cursor = map.Data.GetCursor();
        while (cursor.MoveNext(out var key, out var value))
        {
            callbackFunction.Call(thisArg, [value, key, map]);
        }

        return JsValue.Undefined;
    }

    /// <summary>Map.prototype.get(key)</summary>
    private static JsValue Get(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        ThisMap(thisValue, "get").Data.TryGetValue(GetArgument(arguments, 0), out var value);
        return value;
    }

    /// <summary>Map.prototype.getOrInsert(key, value)</summary>
    private static JsValue GetOrInsert(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var data = ThisMap(thisValue, "getOrInsert").Data;
        var key = GetArgument(arguments, 0);
        if (data.TryGetValue(key, out var existing))
            return existing;

        var value = GetArgument(arguments, 1);
        data.Set(key, value);
        return value;
    }

    /// <summary>Map.prototype.getOrInsertComputed(key, callback)</summary>
    private static JsValue GetOrInsertComputed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var data = ThisMap(thisValue, "getOrInsertComputed").Data;
        var callback = GetArgument(arguments, 1);
        if (!callback.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(callback)} is not a function");

        var key = OrderedHashTable.CanonicalizeKey(GetArgument(arguments, 0));
        if (data.TryGetValue(key, out var existing))
            return existing;

        var value = callback.AsObject().Call(JsValue.Undefined, [key]);

        // The callback may have modified the map: Set overwrites an entry it added
        data.Set(key, value);
        return value;
    }

    /// <summary>Map.prototype.has(key)</summary>
    private static JsValue Has(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisMap(thisValue, "has").Data.ContainsKey(GetArgument(arguments, 0));
    }

    /// <summary>Map.prototype.set(key, value)</summary>
    private static JsValue Set(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var map = ThisMap(thisValue, "set");
        map.Data.Set(GetArgument(arguments, 0), GetArgument(arguments, 1));
        return map;
    }

    /// <summary>get Map.prototype.size</summary>
    private static JsValue GetSize(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisMap(thisValue, "size").Data.Count;
    }

    /// <summary>CreateMapIterator(map, kind)</summary>
    private static JsValue CreateMapIterator(JsValue thisValue, CollectionIteratorKind kind)
    {
        var map = ThisMap(thisValue, kind switch
        {
            CollectionIteratorKind.Keys => "keys",
            CollectionIteratorKind.Values => "values",
            _ => "entries",
        });

        var realm = JsEngine.Current!.CurrentRealm!;
        return new JsCollectionIterator(realm.Intrinsics.MapIteratorPrototype, map.Data, kind, isMapIterator: true);
    }

    /// <summary>%MapIteratorPrototype%.next() and %SetIteratorPrototype%.next(): GeneratorResume(this value, empty, brand)</summary>
    internal static JsValue CollectionIteratorNext(JsValue thisValue, bool isMapIterator)
    {
        if (thisValue.Cell is not JsCollectionIterator iterator || iterator.IsMapIterator != isMapIterator)
            return ThrowHelper.ThrowTypeError<JsValue>($"next method called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        var realm = JsEngine.Current!.CurrentRealm!;
        if (!iterator.MoveNext(out var key, out var value))
            return IteratorOperations.CreateIteratorResultObject(realm, JsValue.Undefined, done: true);

        var result = iterator.IterationKind switch
        {
            CollectionIteratorKind.Keys => key,
            CollectionIteratorKind.Values => value,
            _ => JsArray.Create(realm, [key, value]),
        };

        return IteratorOperations.CreateIteratorResultObject(realm, result, done: false);
    }
}
