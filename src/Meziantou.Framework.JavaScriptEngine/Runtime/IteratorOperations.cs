namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The operations on iterator objects (https://tc39.es/ecma262/#sec-operations-on-iterator-objects).</summary>
internal static class IteratorOperations
{
    private static readonly PropertyKey NextKey = new("next");
    private static readonly PropertyKey DoneKey = new("done");
    private static readonly PropertyKey ValueKey = new("value");
    private static readonly PropertyKey ReturnKey = new("return");
    private static readonly PropertyKey IteratorKey = new(WellKnownSymbols.Iterator);

    /// <summary>GetIteratorFromMethod(obj, method)</summary>
    public static IteratorRecord GetIteratorFromMethod(JsValue value, JsValue method)
    {
        var iterator = JsOperations.Call(method, value, []);
        if (iterator.Cell is not JsObject iteratorObject)
            return ThrowHelper.ThrowTypeError<IteratorRecord>("Result of the Symbol.iterator method is not an object");

        var next = iteratorObject.Get(NextKey);
        return new IteratorRecord(iteratorObject, next);
    }

    /// <summary>GetIterator(obj, sync)</summary>
    public static IteratorRecord GetIterator(JsValue value)
    {
        // An array whose iteration is not observable: no iterator object (it is created if it is needed)
        if (value.Cell is JsArray array && JsEngine.Current?.CurrentRealm is { } realm && ReferenceEquals(array.PrototypeSlot, realm.Intrinsics.ArrayPrototype)
            && !array.TryFindOwnForCache(IteratorKey, out _, out _) && realm.Intrinsics.IsArrayIterationIntact())
        {
            return new IteratorRecord(array, realm);
        }

        var method = JsOperations.GetMethod(value, new PropertyKey(WellKnownSymbols.Iterator));
        if (method is null)
            return ThrowHelper.ThrowTypeError<IteratorRecord>($"{JsOperations.DescribeForError(value)} is not iterable");

        return GetIteratorFromMethod(value, method);
    }

    /// <summary>GetIteratorDirect(obj)</summary>
    public static IteratorRecord GetIteratorDirect(JsObject obj) => new(obj, obj.Get(NextKey));

    /// <summary>IteratorNext(iteratorRecord, value)</summary>
    public static JsObject IteratorNext(IteratorRecord record, JsValue? value = null)
    {
        JsValue result;
        try
        {
            result = value is { } v ? JsOperations.Call(record.NextMethod, record.Iterator, [v]) : JsOperations.Call(record.NextMethod, record.Iterator, []);
        }
        catch (JsException)
        {
            record.Done = true;
            throw;
        }

        if (result.Cell is not JsObject resultObject)
        {
            record.Done = true;
            return ThrowHelper.ThrowTypeError<JsObject>($"Iterator result {JsOperations.DescribeForError(result)} is not an object");
        }

        return resultObject;
    }

    /// <summary>IteratorComplete(iterResult)</summary>
    public static bool IteratorComplete(JsObject result) => JsOperations.ToBoolean(result.Get(DoneKey));

    /// <summary>IteratorValue(iterResult)</summary>
    public static JsValue IteratorValue(JsObject result) => result.Get(ValueKey);

    /// <summary>IteratorStepValue(iteratorRecord): the next value, or false when the iterator is done.</summary>
    public static bool IteratorStepValue(IteratorRecord record, out JsValue value)
    {
        if (record.IsArrayIteration)
        {
            bool hasElement;
            try
            {
                hasElement = record.StepArray(out value);
            }
            catch (JsException)
            {
                record.Done = true;
                throw;
            }

            if (!hasElement)
            {
                record.Done = true;
            }

            return hasElement;
        }

        if (record.NextMethod.Cell is JsBuiltinFunction { IteratorStep: not null } nativeNext)
        {
            // A built-in iterator (arrays, maps, sets): the step without the result object
            bool hasValue;
            try
            {
                hasValue = nativeNext.CallIteratorStep(record.Iterator, out value);
            }
            catch (JsException)
            {
                record.Done = true;
                throw;
            }

            if (!hasValue)
            {
                record.Done = true;
            }

            return hasValue;
        }

        var result = IteratorNext(record);
        bool done;
        try
        {
            done = IteratorComplete(result);
        }
        catch (JsException)
        {
            record.Done = true;
            throw;
        }

        if (done)
        {
            record.Done = true;
            value = default;
            return false;
        }

        try
        {
            value = IteratorValue(result);
        }
        catch (JsException)
        {
            record.Done = true;
            throw;
        }

        return true;
    }

    /// <summary>IteratorClose(iteratorRecord, completion) for a normal completion: errors of return() are thrown.</summary>
    public static void IteratorClose(IteratorRecord record)
    {
        // The array iterators have no return method
        if (record.IsArrayIteration && record.ArrayRealm!.Intrinsics.IsArrayIterationIntact())
            return;

        var returnMethod = JsOperations.GetMethod(record.Iterator, ReturnKey);
        if (returnMethod is null)
            return;

        var result = returnMethod.Call(record.Iterator, []);
        if (!result.IsObject)
        {
            ThrowHelper.ThrowTypeError("Iterator result is not an object");
        }
    }

    /// <summary>IteratorClose(iteratorRecord, completion) for a throw completion: the errors of return() are ignored.</summary>
    public static void IteratorCloseOnThrow(IteratorRecord record)
    {
        if (record.IsArrayIteration && record.ArrayRealm!.Intrinsics.IsArrayIterationIntact())
            return;

        try
        {
            var returnMethod = JsOperations.GetMethod(record.Iterator, ReturnKey);
            returnMethod?.Call(record.Iterator, []);
        }
        catch (JsException)
        {
            // The original exception is rethrown by the caller
        }
    }

    /// <summary>IteratorToList(iteratorRecord)</summary>
    public static List<JsValue> IteratorToList(IteratorRecord record)
    {
        var values = new List<JsValue>();
        while (IteratorStepValue(record, out var value))
        {
            values.Add(value);
        }

        return values;
    }

    /// <summary>IteratorToList(GetIterator(items, sync))</summary>
    public static List<JsValue> IterableToList(JsValue items) => IteratorToList(GetIterator(items));

    /// <summary>CreateIteratorResultObject(value, done)</summary>
    public static JsObject CreateIteratorResultObject(JsRealm realm, JsValue value, bool done)
    {
        var result = new JsObject(realm.Intrinsics.ObjectPrototype);
        result.SetOwnSlot(ValueKey, value, PropertyFlags.Default);
        result.SetOwnSlot(DoneKey, done, PropertyFlags.Default);
        return result;
    }
}
