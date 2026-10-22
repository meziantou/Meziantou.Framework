using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The Iterator constructor, %Iterator.prototype% and its helpers, %IteratorHelperPrototype% and
/// %WrapForValidIteratorPrototype% (https://tc39.es/ecma262/#sec-iteration).
/// </summary>
internal static partial class IteratorBuiltins
{
    private const double MaxSafeInteger = 9007199254740991;

    private static readonly PropertyKey ReturnKey = new("return");
    private static readonly PropertyKey ConstructorKey = new("constructor");

    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("concat", 0, Concat),
        new("from", 1, From),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("drop", 1, Drop),
        new("every", 1, Every),
        new("filter", 1, Filter),
        new("find", 1, Find),
        new("flatMap", 1, FlatMap),
        new("forEach", 1, ForEach),
        new("join", 1, Join),
        new("map", 1, Map),
        new("reduce", 1, Reduce),
        new("some", 1, Some),
        new("take", 1, Take),
        new("toArray", 0, ToArray),
        new(WellKnownSymbols.Iterator, 0, (_, thisValue, _, _) => thisValue),
        new(WellKnownSymbols.Dispose, 0, Dispose),
    ];

    private static readonly BuiltinMethodTable HelperPrototypeMethods =
    [
        new("next", 0, HelperNext),
        new("return", 0, HelperReturn),
    ];

    private static readonly BuiltinMethodTable WrapPrototypeMethods =
    [
        new("next", 0, WrapNext),
        new("return", 0, WrapReturn),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.IteratorPrototype;
        var constructor = realm.CreateBuiltinFunction("Iterator", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethods(constructor, ConstructorMethods);

        realm.DefineAccessor(prototype, ConstructorKey, (f, _, _, _) => f.Realm.Intrinsics.IteratorConstructor, (f, t, a, _) => SetterThatIgnoresPrototypeProperties(f, t, ConstructorKey, a));
        realm.DefineAccessor(prototype, WellKnownSymbols.ToStringTag, (_, _, _, _) => "Iterator", (f, t, a, _) => SetterThatIgnoresPrototypeProperties(f, t, WellKnownSymbols.ToStringTag, a));
        realm.DefineMethods(prototype, PrototypeMethods);
        InitializeProposals(realm, constructor, prototype);

        intrinsics.AddGlobal("Iterator", constructor);
        return constructor;
    }

    /// <summary>Defines the properties of %IteratorHelperPrototype% and %WrapForValidIteratorPrototype% (created on first use).</summary>
    public static void InitializeHelpers(JsRealm realm, JsObject helperPrototype, JsObject wrapPrototype)
    {
        realm.DefineMethods(helperPrototype, HelperPrototypeMethods);
        helperPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Iterator Helper", PropertyFlags.Configurable);
        realm.DefineMethods(wrapPrototype, WrapPrototypeMethods);
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>Iterator()</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null || ReferenceEquals(newTarget, function))
            return ThrowHelper.ThrowTypeError<JsValue>("Abstract class Iterator not directly constructable");

        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, i => i.IteratorPrototype);
        return new JsObject(prototype);
    }

    /// <summary>SetterThatIgnoresPrototypeProperties(thisValue, home, p, v)</summary>
    private static JsValue SetterThatIgnoresPrototypeProperties(JsBuiltinFunction function, JsValue thisValue, PropertyKey key, ReadOnlySpan<JsValue> arguments)
    {
        var value = Arg(arguments, 0);
        if (thisValue.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue>("Iterator.prototype setter called on non-object");

        if (ReferenceEquals(obj, function.Realm.Intrinsics.IteratorPrototype))
            return ThrowHelper.ThrowTypeError<JsValue>($"Cannot assign to read only property '{key}' of Iterator.prototype");

        if (!obj.GetOwnProperty(key, out _))
        {
            obj.CreateDataPropertyOrThrow(key, value);
        }
        else
        {
            obj.Set(key, value, throwOnFailure: true);
        }

        return JsValue.Undefined;
    }

    /// <summary>IteratorStep(iteratorRecord): false when the iterator is done (the value of the result is not read).</summary>
    internal static bool IteratorStep(IteratorRecord record)
    {
        var result = IteratorOperations.IteratorNext(record);
        bool done;
        try
        {
            done = IteratorOperations.IteratorComplete(result);
        }
        catch (JsException)
        {
            record.Done = true;
            throw;
        }

        if (done)
        {
            record.Done = true;
            return false;
        }

        return true;
    }

    /// <summary>GetIteratorFlattenable(obj, primitiveHandling)</summary>
    internal static IteratorRecord GetIteratorFlattenable(JsValue value, bool iterateStrings)
    {
        if (value.Cell is not JsObject)
        {
            if (!iterateStrings || !value.IsString)
                return ThrowHelper.ThrowTypeError<IteratorRecord>($"{JsOperations.DescribeForError(value)} is not an object");
        }

        var method = JsOperations.GetMethod(value, new PropertyKey(WellKnownSymbols.Iterator));
        var iterator = method is null ? value : method.Call(value, []);
        if (iterator.Cell is not JsObject iteratorObject)
            return ThrowHelper.ThrowTypeError<IteratorRecord>("Result of the Symbol.iterator method is not an object");

        return IteratorOperations.GetIteratorDirect(iteratorObject);
    }

    /// <summary>Iterator.from(O)</summary>
    private static JsValue From(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var record = GetIteratorFlattenable(Arg(arguments, 0), iterateStrings: true);
        var intrinsics = function.Realm.Intrinsics;
        if (ObjectOperations.OrdinaryHasInstance(intrinsics.IteratorConstructor, record.Iterator))
            return record.Iterator;

        return new JsWrapForValidIterator(intrinsics.WrapForValidIteratorPrototype, record);
    }

    /// <summary>Iterator.concat(...items)</summary>
    private static JsValue Concat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var iterables = new (JsObject OpenMethod, JsObject Iterable)[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].Cell is not JsObject item)
                return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(arguments[i])} is not an object");

            var method = item.Get(new PropertyKey(WellKnownSymbols.Iterator));
            if (method.Cell is not JsObject { IsCallable: true } methodObject)
                return ThrowHelper.ThrowTypeError<JsValue>("Symbol.iterator is not a function");

            iterables[i] = (methodObject, item);
        }

        return new JsConcatIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterables);
    }

    /// <summary>%WrapForValidIteratorPrototype%.next()</summary>
    private static JsValue WrapNext(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsWrapForValidIterator wrapper)
            return ThrowHelper.ThrowTypeError<JsValue>("next method called on incompatible receiver");

        var record = wrapper.Iterated;
        return JsOperations.Call(record.NextMethod, record.Iterator, []);
    }

    /// <summary>%WrapForValidIteratorPrototype%.return()</summary>
    private static JsValue WrapReturn(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsWrapForValidIterator wrapper)
            return ThrowHelper.ThrowTypeError<JsValue>("return method called on incompatible receiver");

        var iterator = wrapper.Iterated.Iterator;
        var returnMethod = JsOperations.GetMethod(iterator, ReturnKey);
        if (returnMethod is null)
            return IteratorOperations.CreateIteratorResultObject(function.Realm, JsValue.Undefined, done: true);

        return returnMethod.Call(iterator, []);
    }

    /// <summary>%IteratorHelperPrototype%.next(): GeneratorResume(this value, undefined, "Iterator Helper")</summary>
    private static JsValue HelperNext(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsIteratorHelper helper)
            return ThrowHelper.ThrowTypeError<JsValue>("Iterator Helper next method called on incompatible receiver");

        switch (helper.State)
        {
            case IteratorHelperState.Executing:
                return ThrowHelper.ThrowTypeError<JsValue>("Generator is already running");
            case IteratorHelperState.Completed:
                return IteratorOperations.CreateIteratorResultObject(function.Realm, JsValue.Undefined, done: true);
        }

        helper.State = IteratorHelperState.Executing;
        var yielded = false;
        try
        {
            yielded = helper.MoveNext(out var value);
            return IteratorOperations.CreateIteratorResultObject(function.Realm, yielded ? value : JsValue.Undefined, done: !yielded);
        }
        finally
        {
            helper.State = yielded ? IteratorHelperState.SuspendedYield : IteratorHelperState.Completed;
        }
    }

    /// <summary>%IteratorHelperPrototype%.return()</summary>
    private static JsValue HelperReturn(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsIteratorHelper helper)
            return ThrowHelper.ThrowTypeError<JsValue>("Iterator Helper return method called on incompatible receiver");

        switch (helper.State)
        {
            case IteratorHelperState.SuspendedStart:
                helper.State = IteratorHelperState.Completed;
                helper.CloseUnderlyingIterators();
                break;
            case IteratorHelperState.Executing:
                return ThrowHelper.ThrowTypeError<JsValue>("Generator is already running");
            case IteratorHelperState.SuspendedYield:
                helper.State = IteratorHelperState.Executing;
                try
                {
                    helper.ResumeWithReturn();
                }
                finally
                {
                    helper.State = IteratorHelperState.Completed;
                }

                break;
        }

        return IteratorOperations.CreateIteratorResultObject(function.Realm, JsValue.Undefined, done: true);
    }

    private static JsObject ThisObject(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsObject>("Iterator.prototype." + methodName + " called on non-object");

        return obj;
    }

    /// <summary>Closes the iterator (whose next method has not been read) because of an error, then throws the error.</summary>
    private static T CloseAndThrow<T>(JsObject iterator, ErrorType type, string message)
    {
        var exception = ThrowHelper.CreateException(type, message);
        IteratorOperations.IteratorCloseOnThrow(new IteratorRecord(iterator, JsValue.Undefined));
        throw exception;
    }

    /// <summary>The validation of a function argument: a TypeError closes the iterator.</summary>
    private static JsObject GetCallableArgument(JsObject iterator, JsValue value, string methodName)
    {
        if (value.Cell is JsObject { IsCallable: true } callable)
            return callable;

        return CloseAndThrow<JsObject>(iterator, ErrorType.TypeError, $"Iterator.prototype.{methodName}: {JsOperations.DescribeForError(value)} is not a function");
    }

    /// <summary>The validation of the limit of take and drop.</summary>
    private static double GetLimitArgument(JsObject iterator, JsValue limit, string methodName)
    {
        double numLimit;
        try
        {
            numLimit = JsOperations.ToNumber(limit);
        }
        catch (JsException)
        {
            IteratorOperations.IteratorCloseOnThrow(new IteratorRecord(iterator, JsValue.Undefined));
            throw;
        }

        if (double.IsNaN(numLimit) || (double.IsFinite(numLimit) && numLimit > MaxSafeInteger))
            return CloseAndThrow<double>(iterator, ErrorType.RangeError, $"Iterator.prototype.{methodName}: {NumberFormatting.ToString(numLimit)} must be positive");

        var integerLimit = double.IsInfinity(numLimit) ? numLimit : Math.Truncate(numLimit);
        if (integerLimit < 0)
            return CloseAndThrow<double>(iterator, ErrorType.RangeError, $"Iterator.prototype.{methodName}: {NumberFormatting.ToString(numLimit)} must be positive");

        return integerLimit + 0.0;
    }

    /// <summary>Iterator.prototype.map(mapper)</summary>
    private static JsValue Map(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "map");
        var mapper = GetCallableArgument(obj, Arg(arguments, 0), "map");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        return new JsMapIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterated, mapper);
    }

    /// <summary>Iterator.prototype.filter(predicate)</summary>
    private static JsValue Filter(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "filter");
        var predicate = GetCallableArgument(obj, Arg(arguments, 0), "filter");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        return new JsFilterIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterated, predicate);
    }

    /// <summary>Iterator.prototype.take(limit)</summary>
    private static JsValue Take(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "take");
        var limit = GetLimitArgument(obj, Arg(arguments, 0), "take");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        return new JsTakeIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterated, limit);
    }

    /// <summary>Iterator.prototype.drop(limit)</summary>
    private static JsValue Drop(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "drop");
        var limit = GetLimitArgument(obj, Arg(arguments, 0), "drop");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        return new JsDropIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterated, limit);
    }

    /// <summary>Iterator.prototype.flatMap(mapper)</summary>
    private static JsValue FlatMap(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "flatMap");
        var mapper = GetCallableArgument(obj, Arg(arguments, 0), "flatMap");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        return new JsFlatMapIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterated, mapper);
    }

    /// <summary>Calls the function of a consuming method; an exception closes the iterator (IfAbruptCloseIterator).</summary>
    private static JsValue CallClosing(IteratorRecord iterated, JsObject callback, ReadOnlySpan<JsValue> arguments)
    {
        try
        {
            return callback.Call(JsValue.Undefined, arguments);
        }
        catch (JsException)
        {
            IteratorOperations.IteratorCloseOnThrow(iterated);
            throw;
        }
    }

    /// <summary>Iterator.prototype.reduce(reducer [, initialValue])</summary>
    private static JsValue Reduce(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "reduce");
        var reducer = GetCallableArgument(obj, Arg(arguments, 0), "reduce");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        JsValue accumulator;
        double counter;
        if (arguments.Length < 2)
        {
            if (!IteratorOperations.IteratorStepValue(iterated, out accumulator))
                return ThrowHelper.ThrowTypeError<JsValue>("Reduce of empty iterator with no initial value");

            counter = 1;
        }
        else
        {
            accumulator = arguments[1];
            counter = 0;
        }

        while (IteratorOperations.IteratorStepValue(iterated, out var value))
        {
            accumulator = CallClosing(iterated, reducer, [accumulator, value, counter]);
            counter++;
        }

        return accumulator;
    }

    /// <summary>Iterator.prototype.toArray()</summary>
    private static JsValue ToArray(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "toArray");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        var items = IteratorOperations.IteratorToList(iterated);
        return JsArray.Create(function.Realm, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items));
    }

    /// <summary>Iterator.prototype.forEach(procedure)</summary>
    private static JsValue ForEach(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "forEach");
        var procedure = GetCallableArgument(obj, Arg(arguments, 0), "forEach");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        double counter = 0;
        while (IteratorOperations.IteratorStepValue(iterated, out var value))
        {
            CallClosing(iterated, procedure, [value, counter]);
            counter++;
        }

        return JsValue.Undefined;
    }

    /// <summary>Iterator.prototype.some(predicate)</summary>
    private static JsValue Some(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "some");
        var predicate = GetCallableArgument(obj, Arg(arguments, 0), "some");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        double counter = 0;
        while (IteratorOperations.IteratorStepValue(iterated, out var value))
        {
            var result = CallClosing(iterated, predicate, [value, counter]);
            if (JsOperations.ToBoolean(result))
            {
                IteratorOperations.IteratorClose(iterated);
                return true;
            }

            counter++;
        }

        return false;
    }

    /// <summary>Iterator.prototype.every(predicate)</summary>
    private static JsValue Every(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "every");
        var predicate = GetCallableArgument(obj, Arg(arguments, 0), "every");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        double counter = 0;
        while (IteratorOperations.IteratorStepValue(iterated, out var value))
        {
            var result = CallClosing(iterated, predicate, [value, counter]);
            if (!JsOperations.ToBoolean(result))
            {
                IteratorOperations.IteratorClose(iterated);
                return false;
            }

            counter++;
        }

        return true;
    }

    /// <summary>Iterator.prototype.find(predicate)</summary>
    private static JsValue Find(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "find");
        var predicate = GetCallableArgument(obj, Arg(arguments, 0), "find");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        double counter = 0;
        while (IteratorOperations.IteratorStepValue(iterated, out var value))
        {
            var result = CallClosing(iterated, predicate, [value, counter]);
            if (JsOperations.ToBoolean(result))
            {
                IteratorOperations.IteratorClose(iterated);
                return value;
            }

            counter++;
        }

        return JsValue.Undefined;
    }

    /// <summary>Iterator.prototype.join(separator) (proposal-iterator-join)</summary>
    private static JsValue Join(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "join");
        var separatorArgument = Arg(arguments, 0);
        string separator;
        if (separatorArgument.IsUndefined)
        {
            separator = ",";
        }
        else
        {
            try
            {
                separator = JsOperations.ToString(separatorArgument);
            }
            catch (JsException)
            {
                IteratorOperations.IteratorCloseOnThrow(new IteratorRecord(obj, JsValue.Undefined));
                throw;
            }
        }

        var iterated = IteratorOperations.GetIteratorDirect(obj);
        var result = new StringBuilder();
        var first = true;
        while (IteratorOperations.IteratorStepValue(iterated, out var value))
        {
            try
            {
                var str = value.IsNullOrUndefined ? "" : JsOperations.ToString(value);
                var separatorLength = first ? 0 : separator.Length;
                StringOperations.CheckLength((long)result.Length + separatorLength + str.Length);
                if (!first)
                {
                    result.Append(separator);
                }

                first = false;
                result.Append(str);
            }
            catch (JsException)
            {
                IteratorOperations.IteratorCloseOnThrow(iterated);
                throw;
            }
        }

        return result.ToString();
    }

    /// <summary>Iterator.prototype[%Symbol.dispose%]()</summary>
    private static JsValue Dispose(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var returnMethod = JsOperations.GetMethod(thisValue, ReturnKey);
        returnMethod?.Call(thisValue, []);
        return JsValue.Undefined;
    }
}
