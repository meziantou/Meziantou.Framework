namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Set constructor, Set.prototype and %SetIteratorPrototype% (https://tc39.es/ecma262/#sec-set-objects).</summary>
internal static class SetBuiltins
{
    private static readonly PropertyKey AddKey = new("add");
    private static readonly PropertyKey SizeKey = new("size");
    private static readonly PropertyKey HasKey = new("has");
    private static readonly PropertyKey KeysKey = new("keys");

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype, JsObject iteratorPrototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("Set", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, static (_, thisValue, _, _) => thisValue, setter: null);

        realm.DefineMethod(prototype, "add", 1, Add);
        realm.DefineMethod(prototype, "clear", 0, Clear);
        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "delete", 1, Delete);
        realm.DefineMethod(prototype, "difference", 1, Difference);
        realm.DefineMethod(prototype, "entries", 0, static (_, thisValue, _, _) => CreateSetIterator(thisValue, CollectionIteratorKind.Entries, "entries"));
        realm.DefineMethod(prototype, "forEach", 1, ForEach);
        realm.DefineMethod(prototype, "has", 1, Has);
        realm.DefineMethod(prototype, "intersection", 1, Intersection);
        realm.DefineMethod(prototype, "isDisjointFrom", 1, IsDisjointFrom);
        realm.DefineMethod(prototype, "isSubsetOf", 1, IsSubsetOf);
        realm.DefineMethod(prototype, "isSupersetOf", 1, IsSupersetOf);
        var values = realm.CreateBuiltinFunction("values", 0, static (_, thisValue, _, _) => CreateSetIterator(thisValue, CollectionIteratorKind.Values, "values"));
        prototype.DefineBuiltIn("keys", values);
        realm.DefineAccessor(prototype, "size", GetSize, setter: null);
        realm.DefineMethod(prototype, "symmetricDifference", 1, SymmetricDifference);
        realm.DefineMethod(prototype, "union", 1, Union);
        prototype.DefineBuiltIn("values", values);
        prototype.DefineBuiltIn(WellKnownSymbols.Iterator, values);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Set", PropertyFlags.Configurable);

        realm.DefineMethod(iteratorPrototype, "next", 0, static (_, thisValue, _, _) => MapBuiltins.CollectionIteratorNext(thisValue, isMapIterator: false)).IteratorStep = static (_, thisValue, out value) => MapBuiltins.CollectionIteratorStep(thisValue, isMapIterator: false, out value);
        iteratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Set Iterator", PropertyFlags.Configurable);

        intrinsics.AddGlobal("Set", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsSet ThisSet(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsSet set)
            return set;

        return ThrowHelper.ThrowTypeError<JsSet>($"Method Set.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");
    }

    /// <summary>Set([iterable])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Set requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.SetPrototype);
        var set = new JsSet(prototype);
        var iterable = GetArgument(arguments, 0);
        if (iterable.IsNullOrUndefined)
            return set;

        var adder = set.Get(AddKey);
        if (!adder.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("'add' returned for property 'add' of object '#<Set>' is not a function");

        AddValuesFromIterable(set, iterable, adder.AsObject());
        return set;
    }

    /// <summary>The loop of the Set and WeakSet constructors: calls adder for each value of the iterable.</summary>
    internal static void AddValuesFromIterable(JsObject target, JsValue iterable, JsObject adder)
    {
        var iteratorRecord = IteratorOperations.GetIterator(iterable);
        while (IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
        {
            try
            {
                adder.Call(target, [next]);
            }
            catch (JsException)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw;
            }
        }
    }

    /// <summary>Set.prototype.add(value)</summary>
    private static JsValue Add(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "add");
        set.Data.Add(GetArgument(arguments, 0));
        return set;
    }

    /// <summary>Set.prototype.clear()</summary>
    private static JsValue Clear(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        ThisSet(thisValue, "clear").Data.Clear();
        return JsValue.Undefined;
    }

    /// <summary>Set.prototype.delete(value)</summary>
    private static JsValue Delete(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisSet(thisValue, "delete").Data.Remove(GetArgument(arguments, 0));
    }

    /// <summary>Set.prototype.forEach(callback [, thisArg])</summary>
    private static JsValue ForEach(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "forEach");
        var callback = GetArgument(arguments, 0);
        if (!callback.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(callback)} is not a function");

        var callbackFunction = callback.AsObject();
        var thisArg = GetArgument(arguments, 1);
        var cursor = set.Data.GetCursor();
        while (cursor.MoveNext(out var value, out _))
        {
            callbackFunction.Call(thisArg, [value, value, set]);
        }

        return JsValue.Undefined;
    }

    /// <summary>Set.prototype.has(value)</summary>
    private static JsValue Has(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisSet(thisValue, "has").Data.ContainsKey(GetArgument(arguments, 0));
    }

    /// <summary>get Set.prototype.size</summary>
    private static JsValue GetSize(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisSet(thisValue, "size").Data.Count;
    }

    /// <summary>CreateSetIterator(set, kind)</summary>
    private static JsValue CreateSetIterator(JsValue thisValue, CollectionIteratorKind kind, string methodName)
    {
        var set = ThisSet(thisValue, methodName);
        var realm = JsEngine.Current!.CurrentRealm!;
        return new JsCollectionIterator(realm.Intrinsics.SetIteratorPrototype, set.Data, kind, isMapIterator: false);
    }

    /// <summary>Set.prototype.union(other)</summary>
    private static JsValue Union(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "union");
        var other = GetSetRecord(GetArgument(arguments, 0));
        var keysIterator = other.GetKeysIterator();
        var result = set.Data.Clone();
        while (IteratorOperations.IteratorStepValue(keysIterator, out var next))
        {
            result.Add(next);
        }

        return new JsSet(function.Realm.Intrinsics.SetPrototype, result);
    }

    /// <summary>Set.prototype.intersection(other)</summary>
    private static JsValue Intersection(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "intersection");
        var other = GetSetRecord(GetArgument(arguments, 0));
        var result = new OrderedHashTable(hasValues: false);
        if (set.Data.Count <= other.Size)
        {
            // The cursor visits the elements appended by "has" (thisSize is updated after each call)
            var cursor = set.Data.GetCursor();
            while (cursor.MoveNext(out var element, out _))
            {
                if (other.Has(element))
                {
                    result.Add(element);
                }
            }
        }
        else
        {
            var keysIterator = other.GetKeysIterator();
            while (IteratorOperations.IteratorStepValue(keysIterator, out var next))
            {
                next = OrderedHashTable.CanonicalizeKey(next);
                if (set.Data.ContainsKey(next))
                {
                    result.Add(next);
                }
            }
        }

        return new JsSet(function.Realm.Intrinsics.SetPrototype, result);
    }

    /// <summary>Set.prototype.difference(other)</summary>
    private static JsValue Difference(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "difference");
        var other = GetSetRecord(GetArgument(arguments, 0));
        var result = set.Data.Clone();
        if (set.Data.Count <= other.Size)
        {
            // The iteration is over the copy: "has" cannot change it
            var cursor = result.GetCursor();
            while (cursor.MoveNext(out var element, out _))
            {
                if (other.Has(element))
                {
                    result.Remove(element);
                }
            }
        }
        else
        {
            var keysIterator = other.GetKeysIterator();
            while (IteratorOperations.IteratorStepValue(keysIterator, out var next))
            {
                result.Remove(next);
            }
        }

        return new JsSet(function.Realm.Intrinsics.SetPrototype, result);
    }

    /// <summary>Set.prototype.symmetricDifference(other)</summary>
    private static JsValue SymmetricDifference(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "symmetricDifference");
        var other = GetSetRecord(GetArgument(arguments, 0));
        var keysIterator = other.GetKeysIterator();
        var result = set.Data.Clone();
        while (IteratorOperations.IteratorStepValue(keysIterator, out var next))
        {
            next = OrderedHashTable.CanonicalizeKey(next);
            var alreadyInResult = result.ContainsKey(next);
            if (set.Data.ContainsKey(next))
            {
                if (alreadyInResult)
                {
                    result.Remove(next);
                }
            }
            else if (!alreadyInResult)
            {
                result.Add(next);
            }
        }

        return new JsSet(function.Realm.Intrinsics.SetPrototype, result);
    }

    /// <summary>Set.prototype.isSubsetOf(other)</summary>
    private static JsValue IsSubsetOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "isSubsetOf");
        var other = GetSetRecord(GetArgument(arguments, 0));
        if (set.Data.Count > other.Size)
            return false;

        var cursor = set.Data.GetCursor();
        while (cursor.MoveNext(out var element, out _))
        {
            if (!other.Has(element))
                return false;
        }

        return true;
    }

    /// <summary>Set.prototype.isSupersetOf(other)</summary>
    private static JsValue IsSupersetOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "isSupersetOf");
        var other = GetSetRecord(GetArgument(arguments, 0));
        if (set.Data.Count < other.Size)
            return false;

        var keysIterator = other.GetKeysIterator();
        while (IteratorOperations.IteratorStepValue(keysIterator, out var next))
        {
            if (!set.Data.ContainsKey(next))
            {
                IteratorOperations.IteratorClose(keysIterator);
                return false;
            }
        }

        return true;
    }

    /// <summary>Set.prototype.isDisjointFrom(other)</summary>
    private static JsValue IsDisjointFrom(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var set = ThisSet(thisValue, "isDisjointFrom");
        var other = GetSetRecord(GetArgument(arguments, 0));
        if (set.Data.Count <= other.Size)
        {
            var cursor = set.Data.GetCursor();
            while (cursor.MoveNext(out var element, out _))
            {
                if (other.Has(element))
                    return false;
            }
        }
        else
        {
            var keysIterator = other.GetKeysIterator();
            while (IteratorOperations.IteratorStepValue(keysIterator, out var next))
            {
                if (set.Data.ContainsKey(next))
                {
                    IteratorOperations.IteratorClose(keysIterator);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>GetSetRecord(obj)</summary>
    private static SetRecord GetSetRecord(JsValue value)
    {
        if (value.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<SetRecord>($"{JsOperations.DescribeForError(value)} is not an object");

        var rawSize = obj.Get(SizeKey);
        var numberSize = JsOperations.ToNumber(rawSize);
        if (double.IsNaN(numberSize))
            return ThrowHelper.ThrowTypeError<SetRecord>("The 'size' property must be a number");

        var intSize = JsOperations.ToIntegerOrInfinity(numberSize);
        if (intSize < 0)
            return ThrowHelper.ThrowRangeError<SetRecord>("The 'size' property must not be negative");

        var has = obj.Get(HasKey);
        if (!has.IsCallable)
            return ThrowHelper.ThrowTypeError<SetRecord>("The 'has' property must be a function");

        var keys = obj.Get(KeysKey);
        if (!keys.IsCallable)
            return ThrowHelper.ThrowTypeError<SetRecord>("The 'keys' property must be a function");

        return new SetRecord(obj, intSize, has.AsObject(), keys);
    }

    /// <summary>A Set Record (https://tc39.es/ecma262/#sec-set-records).</summary>
    private readonly struct SetRecord(JsObject setObject, double size, JsObject has, JsValue keys)
    {
        public double Size { get; } = size;

        /// <summary>ToBoolean(? Call(setRecord.[[Has]], setRecord.[[SetObject]], [value]))</summary>
        public bool Has(JsValue value) => JsOperations.ToBoolean(has.Call(setObject, [value]));

        /// <summary>GetIteratorFromMethod(setRecord.[[SetObject]], setRecord.[[Keys]])</summary>
        public IteratorRecord GetKeysIterator() => IteratorOperations.GetIteratorFromMethod(setObject, keys);
    }
}
