using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Array constructor, Array.prototype and %ArrayIteratorPrototype% (https://tc39.es/ecma262/#sec-array-objects).</summary>
internal static class ArrayBuiltins
{
    private const double MaxSafeInteger = ArrayOperations.MaxSafeInteger;

    private static readonly PropertyKey JoinKey = new("join");
    private static readonly PropertyKey ToStringKey = new("toString");
    private static readonly PropertyKey ToLocaleStringKey = new("toLocaleString");

    public static (JsFunction Constructor, JsFunction Values, JsFunction ToStringFunction) Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.ArrayPrototype;
        var constructor = realm.CreateBuiltinFunction("Array", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "from", 1, From);
        realm.DefineMethod(constructor, "isArray", 1, (_, _, a, _) => JsOperations.IsArray(Arg(a, 0)));
        realm.DefineMethod(constructor, "of", 0, Of);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, (_, t, _, _) => t, setter: null);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "at", 1, At);
        realm.DefineMethod(prototype, "concat", 1, Concat);
        realm.DefineMethod(prototype, "copyWithin", 2, CopyWithin);
        realm.DefineMethod(prototype, "entries", 0, (f, t, _, _) => JsArrayIterator.Create(f.Realm, JsOperations.ToObject(t), ArrayIteratorKind.Entries));
        realm.DefineMethod(prototype, "every", 1, Every);
        realm.DefineMethod(prototype, "fill", 1, Fill);
        realm.DefineMethod(prototype, "filter", 1, Filter);
        realm.DefineMethod(prototype, "find", 1, (_, t, a, _) => FindViaPredicate(t, a, ascending: true, "find").Value);
        realm.DefineMethod(prototype, "findIndex", 1, (_, t, a, _) => FindViaPredicate(t, a, ascending: true, "findIndex").Index);
        realm.DefineMethod(prototype, "findLast", 1, (_, t, a, _) => FindViaPredicate(t, a, ascending: false, "findLast").Value);
        realm.DefineMethod(prototype, "findLastIndex", 1, (_, t, a, _) => FindViaPredicate(t, a, ascending: false, "findLastIndex").Index);
        realm.DefineMethod(prototype, "flat", 0, Flat);
        realm.DefineMethod(prototype, "flatMap", 1, FlatMap);
        realm.DefineMethod(prototype, "forEach", 1, ForEach);
        realm.DefineMethod(prototype, "includes", 1, Includes);
        realm.DefineMethod(prototype, "indexOf", 1, IndexOf);
        realm.DefineMethod(prototype, "join", 1, Join);
        realm.DefineMethod(prototype, "keys", 0, (f, t, _, _) => JsArrayIterator.Create(f.Realm, JsOperations.ToObject(t), ArrayIteratorKind.Keys));
        realm.DefineMethod(prototype, "lastIndexOf", 1, LastIndexOf);
        realm.DefineMethod(prototype, "map", 1, Map);
        realm.DefineMethod(prototype, "pop", 0, Pop);
        realm.DefineMethod(prototype, "push", 1, Push);
        realm.DefineMethod(prototype, "reduce", 1, (_, t, a, _) => Reduce(t, a, ascending: true));
        realm.DefineMethod(prototype, "reduceRight", 1, (_, t, a, _) => Reduce(t, a, ascending: false));
        realm.DefineMethod(prototype, "reverse", 0, Reverse);
        realm.DefineMethod(prototype, "shift", 0, Shift);
        realm.DefineMethod(prototype, "slice", 2, Slice);
        realm.DefineMethod(prototype, "some", 1, Some);
        realm.DefineMethod(prototype, "sort", 1, Sort);
        realm.DefineMethod(prototype, "splice", 2, Splice);
        realm.DefineMethod(prototype, "toLocaleString", 0, ToLocaleString);
        realm.DefineMethod(prototype, "toReversed", 0, ToReversed);
        realm.DefineMethod(prototype, "toSorted", 1, ToSorted);
        realm.DefineMethod(prototype, "toSpliced", 2, ToSpliced);
        var toString = realm.DefineEagerMethod(prototype, "toString", 0, ToString);
        realm.DefineMethod(prototype, "unshift", 1, Unshift);
        var values = realm.DefineEagerMethod(prototype, "values", 0, (f, t, _, _) => JsArrayIterator.Create(f.Realm, JsOperations.ToObject(t), ArrayIteratorKind.Values));
        realm.DefineMethod(prototype, "with", 2, With);
        prototype.DefineBuiltIn(WellKnownSymbols.Iterator, values);
        prototype.DefineBuiltIn(WellKnownSymbols.Unscopables, CreateUnscopables(), PropertyFlags.Configurable);

        // %Object.prototype.toString%, the fallback of Array.prototype.toString
        if (intrinsics.ObjectPrototype.GetOwnProperty(ToStringKey, out var objectToString) && objectToString.Value.Cell is JsObject { IsCallable: true } objectToStringFunction)
        {
            toString.State = objectToStringFunction;
        }

        var iteratorPrototype = intrinsics.ArrayIteratorPrototype;
        realm.DefineEagerMethod(iteratorPrototype, "next", 0, IteratorNext).IteratorStep = IteratorStep;
        iteratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Array Iterator", PropertyFlags.Configurable);

        intrinsics.AddGlobal("Array", constructor);
        return (constructor, values, toString);
    }

    private static JsObject CreateUnscopables()
    {
        var unscopables = new JsObject(prototype: null);
        foreach (var name in (ReadOnlySpan<string>)["at", "copyWithin", "entries", "fill", "find", "findIndex", "findLast", "findLastIndex", "flat", "flatMap", "includes", "keys", "toReversed", "toSorted", "toSpliced", "values"])
        {
            unscopables.CreateDataPropertyOrThrow(name, JsValue.True);
        }

        return unscopables;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsObject GetCallback(JsValue value, string methodName)
    {
        if (value.Cell is JsObject { IsCallable: true } callable)
            return callable;

        return ThrowHelper.ThrowTypeError<JsObject>($"Array.prototype.{methodName}: {JsOperations.DescribeForError(value)} is not a function");
    }

    /// <summary>The resolution of a relative index (start, end...): negative values count from the end, the result is in [0, length].</summary>
    private static long RelativeIndex(double relative, long length)
    {
        if (relative < 0)
            return (long)Math.Max(length + relative, 0);

        return (long)Math.Min(relative, length);
    }

    private static void ThrowTooLong() => ThrowHelper.ThrowTypeError("The length of the array exceeds 2^53-1");

    // ------------------------------------------------------------------------------------------------------------------
    // The constructor
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Array(...values)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, i => i.ArrayPrototype);
        if (arguments.Length == 0)
            return new JsArray(prototype);

        if (arguments.Length == 1)
        {
            var length = arguments[0];
            if (!length.IsNumber)
            {
                var array = new JsArray(prototype);
                array.CreateDataPropertyOrThrow(new PropertyKey(0), length);
                return array;
            }

            var intLength = JsOperations.ToUint32(length);
            if (intLength != length.NumberValue)
            {
                ThrowHelper.ThrowRangeError("Invalid array length");
            }

            return new JsArray(prototype, intLength);
        }

        var result = ArrayOperations.ArrayCreate(arguments.Length, prototype);
        for (var k = 0; k < arguments.Length; k++)
        {
            result.SetOwnSlot(new PropertyKey((uint)k), arguments[k], PropertyFlags.Default);
        }

        return result;
    }

    /// <summary>Array.from(items [, mapper [, thisArg]])</summary>
    private static JsValue From(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var items = Arg(arguments, 0);
        var mapperArgument = Arg(arguments, 1);
        var thisArg = Arg(arguments, 2);
        JsObject? mapper = null;
        if (!mapperArgument.IsUndefined)
        {
            mapper = mapperArgument.Cell as JsObject;
            if (mapper is not { IsCallable: true })
                return ThrowHelper.ThrowTypeError<JsValue>($"Array.from: {JsOperations.DescribeForError(mapperArgument)} is not a function");
        }

        var usingIterator = JsOperations.GetMethod(items, new PropertyKey(WellKnownSymbols.Iterator));
        if (usingIterator is not null)
        {
            var array = thisValue.Cell is JsObject { IsConstructor: true } c ? JsOperations.Construct(c, []).AsObject() : ArrayOperations.ArrayCreate(0);
            var iteratorRecord = IteratorOperations.GetIteratorFromMethod(items, usingIterator);
            for (long k = 0; ; k++)
            {
                if (k >= MaxSafeInteger)
                {
                    var exception = ThrowHelper.CreateException(ErrorType.TypeError, "The length of the array exceeds 2^53-1");
                    IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                    throw exception;
                }

                if (!IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
                {
                    ArrayOperations.SetLength(array, k);
                    return array;
                }

                try
                {
                    var mappedValue = mapper is null ? next : mapper.Call(thisArg, [next, (double)k]);
                    ArrayOperations.CreateElement(array, k, mappedValue);
                }
                catch (JsException)
                {
                    IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                    throw;
                }
            }
        }

        var arrayLike = JsOperations.ToObject(items);
        var length = JsOperations.LengthOfArrayLike(arrayLike);
        var result = thisValue.Cell is JsObject { IsConstructor: true } constructor ? JsOperations.Construct(constructor, [(double)length]).AsObject() : ArrayOperations.ArrayCreate(length);
        for (long k = 0; k < length; k++)
        {
            var kValue = ArrayOperations.GetElement(arrayLike, k);
            var mappedValue = mapper is null ? kValue : mapper.Call(thisArg, [kValue, (double)k]);
            ArrayOperations.CreateElement(result, k, mappedValue);
        }

        ArrayOperations.SetLength(result, length);
        return result;
    }

    /// <summary>Array.of(...items)</summary>
    private static JsValue Of(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var length = arguments.Length;
        var array = thisValue.Cell is JsObject { IsConstructor: true } c ? JsOperations.Construct(c, [length]).AsObject() : ArrayOperations.ArrayCreate(length);
        for (var k = 0; k < length; k++)
        {
            ArrayOperations.CreateElement(array, k, arguments[k]);
        }

        ArrayOperations.SetLength(array, length);
        return array;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Array.prototype
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Array.prototype.at(index)</summary>
    private static JsValue At(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var relativeIndex = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        var k = relativeIndex >= 0 ? relativeIndex : length + relativeIndex;
        if (k < 0 || k >= length)
            return JsValue.Undefined;

        return ArrayOperations.GetElement(obj, (long)k);
    }

    /// <summary>Array.prototype.concat(...items)</summary>
    private static JsValue Concat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var array = ArrayOperations.ArraySpeciesCreate(obj, 0);
        long n = 0;
        for (var i = -1; i < arguments.Length; i++)
        {
            var element = i < 0 ? new JsValue(obj) : arguments[i];
            if (ArrayOperations.IsConcatSpreadable(element))
            {
                var source = element.AsObject();
                var length = JsOperations.LengthOfArrayLike(source);
                if (n + length > MaxSafeInteger)
                {
                    ThrowTooLong();
                }

                for (long k = 0; k < length; k++)
                {
                    var next = ArrayOperations.SkipHoles(source, k, length);
                    if (next >= length)
                        break;

                    if (ArrayOperations.TryGetElement(source, next, out var subElement))
                    {
                        ArrayOperations.CreateElement(array, n + next, subElement);
                    }

                    k = next;
                }

                n += length;
            }
            else
            {
                if (n >= MaxSafeInteger)
                {
                    ThrowTooLong();
                }

                ArrayOperations.CreateElement(array, n, element);
                n++;
            }
        }

        ArrayOperations.SetLength(array, n);
        return array;
    }

    /// <summary>Array.prototype.copyWithin(target, start [, end])</summary>
    private static JsValue CopyWithin(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var to = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var from = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 1)), length);
        var end = Arg(arguments, 2);
        var final = end.IsUndefined ? length : RelativeIndex(JsOperations.ToIntegerOrInfinity(end), length);
        var count = Math.Min(final - from, length - to);
        long direction = 1;
        if (from < to && to < from + count)
        {
            direction = -1;
            from += count - 1;
            to += count - 1;
        }

        while (count > 0)
        {
            if (ArrayOperations.TryGetElement(obj, from, out var fromValue))
            {
                ArrayOperations.SetElement(obj, to, fromValue);
            }
            else
            {
                ArrayOperations.DeleteElement(obj, to);
            }

            from += direction;
            to += direction;
            count--;
        }

        return obj;
    }

    /// <summary>Array.prototype.every(callback [, thisArg])</summary>
    private static JsValue Every(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var callback = GetCallback(Arg(arguments, 0), "every");
        var thisArg = Arg(arguments, 1);
        for (long k = 0; k < length; k++)
        {
            k = ArrayOperations.SkipHoles(obj, k, length);
            if (k >= length)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var kValue) && !JsOperations.ToBoolean(callback.Call(thisArg, [kValue, (double)k, obj])))
                return false;
        }

        return true;
    }

    /// <summary>Array.prototype.some(callback [, thisArg])</summary>
    private static JsValue Some(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var callback = GetCallback(Arg(arguments, 0), "some");
        var thisArg = Arg(arguments, 1);
        for (long k = 0; k < length; k++)
        {
            k = ArrayOperations.SkipHoles(obj, k, length);
            if (k >= length)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var kValue) && JsOperations.ToBoolean(callback.Call(thisArg, [kValue, (double)k, obj])))
                return true;
        }

        return false;
    }

    /// <summary>Array.prototype.forEach(callback [, thisArg])</summary>
    private static JsValue ForEach(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var callback = GetCallback(Arg(arguments, 0), "forEach");
        var thisArg = Arg(arguments, 1);
        for (long k = 0; k < length; k++)
        {
            k = ArrayOperations.SkipHoles(obj, k, length);
            if (k >= length)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var kValue))
            {
                callback.Call(thisArg, [kValue, (double)k, obj]);
            }
        }

        return JsValue.Undefined;
    }

    /// <summary>Array.prototype.fill(value [, start [, end]])</summary>
    private static JsValue Fill(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var value = Arg(arguments, 0);
        var k = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 1)), length);
        var end = Arg(arguments, 2);
        var final = end.IsUndefined ? length : RelativeIndex(JsOperations.ToIntegerOrInfinity(end), length);
        for (; k < final; k++)
        {
            ArrayOperations.SetElement(obj, k, value);
        }

        return obj;
    }

    /// <summary>Array.prototype.filter(callback [, thisArg])</summary>
    private static JsValue Filter(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var callback = GetCallback(Arg(arguments, 0), "filter");
        var thisArg = Arg(arguments, 1);
        var array = ArrayOperations.ArraySpeciesCreate(obj, 0);
        long to = 0;
        for (long k = 0; k < length; k++)
        {
            k = ArrayOperations.SkipHoles(obj, k, length);
            if (k >= length)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var kValue) && JsOperations.ToBoolean(callback.Call(thisArg, [kValue, (double)k, obj])))
            {
                ArrayOperations.CreateElement(array, to, kValue);
                to++;
            }
        }

        return array;
    }

    /// <summary>FindViaPredicate(O, len, direction, predicate, thisArg) for find, findIndex, findLast and findLastIndex.</summary>
    private static (JsValue Index, JsValue Value) FindViaPredicate(JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool ascending, string methodName)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var predicate = GetCallback(Arg(arguments, 0), methodName);
        var thisArg = Arg(arguments, 1);
        for (long i = 0; i < length; i++)
        {
            var k = ascending ? i : length - 1 - i;
            var kValue = ArrayOperations.GetElement(obj, k);
            if (JsOperations.ToBoolean(predicate.Call(thisArg, [kValue, (double)k, obj])))
                return ((double)k, kValue);
        }

        return (-1, JsValue.Undefined);
    }

    /// <summary>Array.prototype.flat([depth])</summary>
    private static JsValue Flat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var sourceLength = JsOperations.LengthOfArrayLike(obj);
        double depthNum = 1;
        var depth = Arg(arguments, 0);
        if (!depth.IsUndefined)
        {
            depthNum = JsOperations.ToIntegerOrInfinity(depth);
            if (depthNum < 0)
            {
                depthNum = 0;
            }
        }

        var array = ArrayOperations.ArraySpeciesCreate(obj, 0);
        FlattenIntoArray(array, obj, sourceLength, 0, depthNum, mapper: null, thisArg: default);
        return array;
    }

    /// <summary>Array.prototype.flatMap(mapper [, thisArg])</summary>
    private static JsValue FlatMap(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var sourceLength = JsOperations.LengthOfArrayLike(obj);
        var mapper = GetCallback(Arg(arguments, 0), "flatMap");
        var array = ArrayOperations.ArraySpeciesCreate(obj, 0);
        FlattenIntoArray(array, obj, sourceLength, 0, 1, mapper, Arg(arguments, 1));
        return array;
    }

    /// <summary>FlattenIntoArray(target, source, sourceLen, start, depth [, mapperFunction, thisArg])</summary>
    private static long FlattenIntoArray(JsObject target, JsObject source, long sourceLength, long start, double depth, JsObject? mapper, JsValue thisArg)
    {
        // The recursion follows the nesting of the arrays: it is bounded like the calls
        var engine = JsEngine.Current!;
        engine.CheckCallDepth();
        engine.CallDepth++;
        try
        {
            var targetIndex = start;
            for (long sourceIndex = 0; sourceIndex < sourceLength; sourceIndex++)
            {
                sourceIndex = ArrayOperations.SkipHoles(source, sourceIndex, sourceLength);
                if (sourceIndex >= sourceLength)
                    break;

                if (!ArrayOperations.TryGetElement(source, sourceIndex, out var element))
                    continue;

                if (mapper is not null)
                {
                    element = mapper.Call(thisArg, [element, (double)sourceIndex, source]);
                }

                if (depth > 0 && JsOperations.IsArray(element))
                {
                    var newDepth = double.IsPositiveInfinity(depth) ? depth : depth - 1;
                    var elementObject = element.AsObject();
                    var elementLength = JsOperations.LengthOfArrayLike(elementObject);
                    targetIndex = FlattenIntoArray(target, elementObject, elementLength, targetIndex, newDepth, mapper: null, thisArg: default);
                }
                else
                {
                    if (targetIndex >= MaxSafeInteger)
                    {
                        ThrowTooLong();
                    }

                    ArrayOperations.CreateElement(target, targetIndex, element);
                    targetIndex++;
                }
            }

            return targetIndex;
        }
        finally
        {
            engine.CallDepth--;
        }
    }

    /// <summary>Array.prototype.includes(searchElement [, fromIndex])</summary>
    private static JsValue Includes(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        if (length == 0)
            return false;

        var n = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        if (double.IsPositiveInfinity(n))
            return false;

        var k = RelativeIndex(n, length);
        var searchElement = Arg(arguments, 0);
        for (; k < length; k++)
        {
            var next = ArrayOperations.SkipHoles(obj, k, length);
            if (next > k && searchElement.IsUndefined)
                return true; // a hole is read as undefined

            k = next;
            if (k >= length)
                break;

            if (JsOperations.SameValueZero(ArrayOperations.GetElement(obj, k), searchElement))
                return true;
        }

        return false;
    }

    /// <summary>Array.prototype.indexOf(searchElement [, fromIndex])</summary>
    private static JsValue IndexOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        if (length == 0)
            return -1;

        var n = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        if (double.IsPositiveInfinity(n))
            return -1;

        var searchElement = Arg(arguments, 0);
        for (var k = RelativeIndex(n, length); k < length; k++)
        {
            k = ArrayOperations.SkipHoles(obj, k, length);
            if (k >= length)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var element) && JsOperations.IsStrictlyEqual(element, searchElement))
                return k;
        }

        return -1;
    }

    /// <summary>Array.prototype.lastIndexOf(searchElement [, fromIndex])</summary>
    private static JsValue LastIndexOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        if (length == 0)
            return -1;

        var n = arguments.Length > 1 ? JsOperations.ToIntegerOrInfinity(arguments[1]) : length - 1;
        if (double.IsNegativeInfinity(n))
            return -1;

        var k = n >= 0 ? (long)Math.Min(n, length - 1) : (long)Math.Max(length + n, -1);
        var searchElement = Arg(arguments, 0);
        for (; k >= 0; k--)
        {
            k = ArrayOperations.SkipHolesBackward(obj, k);
            if (k < 0)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var element) && JsOperations.IsStrictlyEqual(element, searchElement))
                return k;
        }

        return -1;
    }

    /// <summary>Array.prototype.join(separator)</summary>
    private static JsValue Join(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var separatorArgument = Arg(arguments, 0);
        var separator = separatorArgument.IsUndefined ? "," : JsOperations.ToString(separatorArgument);
        if (length == 0)
            return JsValue.FromJsString(JsString.Empty);

        // The result would be too long: the elements are not read (the limit of the length of the strings is implementation-defined)
        if (separator.Length > 0 && (length - 1) * (double)separator.Length > JsString.MaxLength)
        {
            StringOperations.ThrowInvalidLength();
        }

        // An array that contains itself is joined as an empty string, as in the browsers, instead of an infinite recursion
        // (e.g. [a] where a = [a] is converted to the string ""): the specification has no cycle detection
        var joinStack = function.Realm.Engine.ArrayJoinStack ??= [];
        if (joinStack.Contains(obj))
            return JsValue.FromJsString(JsString.Empty);

        joinStack.Add(obj);
        try
        {
            return JoinElements(obj, length, separator);
        }
        finally
        {
            joinStack.RemoveAt(joinStack.Count - 1);
        }
    }

    private static JsValue JoinElements(JsObject obj, long length, string separator)
    {
        if (length == 1)
        {
            var element = ArrayOperations.GetElement(obj, 0);
            return element.IsNullOrUndefined ? JsValue.FromJsString(JsString.Empty) : JsValue.FromJsString(JsOperations.ToJsString(element));
        }

        var result = new StringBuilder();
        for (long k = 0; k < length; k++)
        {
            var next = ArrayOperations.SkipHoles(obj, k, length);
            if (next > k)
            {
                // The holes are empty strings
                var skipped = next - k;
                if (k == 0)
                {
                    skipped--;
                }

                StringOperations.CheckLength(result.Length + (skipped * separator.Length));
                if (separator.Length == 1)
                {
                    result.Append(separator[0], (int)skipped);
                }
                else if (separator.Length > 1)
                {
                    for (long i = 0; i < skipped; i++)
                    {
                        result.Append(separator);
                    }
                }

                if (next >= length)
                    break;

                k = next;
                result.Append(separator);
            }
            else if (k > 0)
            {
                StringOperations.CheckLength((long)result.Length + separator.Length);
                result.Append(separator);
            }

            var element = ArrayOperations.GetElement(obj, k);
            if (!element.IsNullOrUndefined)
            {
                var str = JsOperations.ToString(element);
                StringOperations.CheckLength((long)result.Length + str.Length);
                result.Append(str);
            }
        }

        return result.ToString();
    }

    /// <summary>Array.prototype.map(callback [, thisArg])</summary>
    private static JsValue Map(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var callback = GetCallback(Arg(arguments, 0), "map");
        var thisArg = Arg(arguments, 1);
        var array = ArrayOperations.ArraySpeciesCreate(obj, length);
        for (long k = 0; k < length; k++)
        {
            k = ArrayOperations.SkipHoles(obj, k, length);
            if (k >= length)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var kValue))
            {
                var mappedValue = callback.Call(thisArg, [kValue, (double)k, obj]);
                ArrayOperations.CreateElement(array, k, mappedValue);
            }
        }

        return array;
    }

    /// <summary>Array.prototype.pop()</summary>
    private static JsValue Pop(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        if (length == 0)
        {
            ArrayOperations.SetLength(obj, 0);
            return JsValue.Undefined;
        }

        var newLength = length - 1;
        var element = ArrayOperations.GetElement(obj, newLength);
        ArrayOperations.DeleteElement(obj, newLength);
        ArrayOperations.SetLength(obj, newLength);
        return element;
    }

    /// <summary>Array.prototype.push(...items)</summary>
    private static JsValue Push(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        if (length + arguments.Length > MaxSafeInteger)
        {
            ThrowTooLong();
        }

        // Fast path: appending to an array whose prototypes have no elements creates own data properties
        if (obj is JsArray array && array.IsLengthWritable && array.IsExtensibleSlot && length + arguments.Length < uint.MaxValue && ArrayOperations.PrototypesHaveNoElements(array))
        {
            foreach (var item in arguments)
            {
                array.Push(item);
            }

            return array.Length;
        }

        foreach (var item in arguments)
        {
            ArrayOperations.SetElement(obj, length, item);
            length++;
        }

        ArrayOperations.SetLength(obj, length);
        return length;
    }

    /// <summary>Array.prototype.reduce(callback [, initialValue]) and Array.prototype.reduceRight(callback [, initialValue])</summary>
    private static JsValue Reduce(JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool ascending)
    {
        var methodName = ascending ? "reduce" : "reduceRight";
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var callback = GetCallback(Arg(arguments, 0), methodName);
        if (length == 0 && arguments.Length < 2)
            return ThrowHelper.ThrowTypeError<JsValue>($"Array.prototype.{methodName} of empty array with no initial value");

        var k = ascending ? 0 : length - 1;
        var step = ascending ? 1 : -1;
        JsValue accumulator = default;
        if (arguments.Length >= 2)
        {
            accumulator = arguments[1];
        }
        else
        {
            var present = false;
            while (!present && k >= 0 && k < length)
            {
                k = ascending ? ArrayOperations.SkipHoles(obj, k, length) : ArrayOperations.SkipHolesBackward(obj, k);
                if (k < 0 || k >= length)
                    break;

                present = ArrayOperations.TryGetElement(obj, k, out accumulator);
                k += step;
            }

            if (!present)
                return ThrowHelper.ThrowTypeError<JsValue>($"Array.prototype.{methodName} of empty array with no initial value");
        }

        while (k >= 0 && k < length)
        {
            k = ascending ? ArrayOperations.SkipHoles(obj, k, length) : ArrayOperations.SkipHolesBackward(obj, k);
            if (k < 0 || k >= length)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var kValue))
            {
                accumulator = callback.Call(JsValue.Undefined, [accumulator, kValue, (double)k, obj]);
            }

            k += step;
        }

        return accumulator;
    }

    /// <summary>Array.prototype.reverse()</summary>
    private static JsValue Reverse(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        if (ArrayOperations.IsPackedArray(obj, length, out var packed))
        {
            packed.DenseElements.AsSpan(0, (int)length).Reverse();
            return obj;
        }

        var middle = length / 2;
        for (long lower = 0; lower != middle; lower++)
        {
            var upper = length - lower - 1;
            var lowerExists = ArrayOperations.TryGetElement(obj, lower, out var lowerValue);
            var upperExists = ArrayOperations.TryGetElement(obj, upper, out var upperValue);
            if (lowerExists && upperExists)
            {
                ArrayOperations.SetElement(obj, lower, upperValue);
                ArrayOperations.SetElement(obj, upper, lowerValue);
            }
            else if (upperExists)
            {
                ArrayOperations.SetElement(obj, lower, upperValue);
                ArrayOperations.DeleteElement(obj, upper);
            }
            else if (lowerExists)
            {
                ArrayOperations.DeleteElement(obj, lower);
                ArrayOperations.SetElement(obj, upper, lowerValue);
            }
        }

        return obj;
    }

    /// <summary>Array.prototype.shift()</summary>
    private static JsValue Shift(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        if (length == 0)
        {
            ArrayOperations.SetLength(obj, 0);
            return JsValue.Undefined;
        }

        var first = ArrayOperations.GetElement(obj, 0);
        MoveElements(obj, length, actualStart: 0, actualDeleteCount: 1, itemCount: 0);
        ArrayOperations.SetLength(obj, length - 1);
        return first;
    }

    /// <summary>Array.prototype.slice(start, end)</summary>
    private static JsValue Slice(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var k = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var end = Arg(arguments, 1);
        var final = end.IsUndefined ? length : RelativeIndex(JsOperations.ToIntegerOrInfinity(end), length);
        var count = Math.Max(final - k, 0);
        var array = ArrayOperations.ArraySpeciesCreate(obj, count);
        var start = k;
        for (; k < final; k++)
        {
            k = ArrayOperations.SkipHoles(obj, k, final);
            if (k >= final)
                break;

            if (ArrayOperations.TryGetElement(obj, k, out var kValue))
            {
                ArrayOperations.CreateElement(array, k - start, kValue);
            }
        }

        ArrayOperations.SetLength(array, count);
        return array;
    }

    /// <summary>Array.prototype.sort(comparator)</summary>
    private static JsValue Sort(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var comparer = GetComparer(Arg(arguments, 0), "sort");
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var sortedList = ArrayOperations.SortIndexedProperties(obj, length, comparer, skipHoles: true);
        var itemCount = sortedList.Count;
        long j = 0;
        for (; j < itemCount; j++)
        {
            ArrayOperations.SetElement(obj, j, sortedList[(int)j]);
        }

        // The holes are moved at the end
        for (; j < length; j++)
        {
            j = ArrayOperations.SkipHoles(obj, j, length);
            if (j >= length)
                break;

            ArrayOperations.DeleteElement(obj, j);
        }

        return obj;
    }

    private static JsObject? GetComparer(JsValue comparer, string methodName)
    {
        if (comparer.IsUndefined)
            return null;

        if (comparer.Cell is JsObject { IsCallable: true } callable)
            return callable;

        return ThrowHelper.ThrowTypeError<JsObject>($"Array.prototype.{methodName}: The comparison function must be either a function or undefined");
    }

    /// <summary>Array.prototype.splice(start, deleteCount, ...items)</summary>
    private static JsValue Splice(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var actualStart = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var items = arguments.Length > 2 ? arguments[2..] : ReadOnlySpan<JsValue>.Empty;
        var itemCount = items.Length;
        long actualDeleteCount;
        if (arguments.Length == 0)
        {
            actualDeleteCount = 0;
        }
        else if (arguments.Length == 1)
        {
            actualDeleteCount = length - actualStart;
        }
        else
        {
            var deleteCount = JsOperations.ToIntegerOrInfinity(arguments[1]);
            actualDeleteCount = (long)Math.Clamp(deleteCount, 0, length - actualStart);
        }

        if (length + itemCount - actualDeleteCount > MaxSafeInteger)
        {
            ThrowTooLong();
        }

        var array = ArrayOperations.ArraySpeciesCreate(obj, actualDeleteCount);
        for (long k = 0; k < actualDeleteCount; k++)
        {
            k = ArrayOperations.SkipHoles(obj, actualStart + k, actualStart + actualDeleteCount) - actualStart;
            if (k >= actualDeleteCount)
                break;

            if (ArrayOperations.TryGetElement(obj, actualStart + k, out var fromValue))
            {
                ArrayOperations.CreateElement(array, k, fromValue);
            }
        }

        ArrayOperations.SetLength(array, actualDeleteCount);
        MoveElements(obj, length, actualStart, actualDeleteCount, itemCount);

        for (var i = 0; i < items.Length; i++)
        {
            ArrayOperations.SetElement(obj, actualStart + i, items[i]);
        }

        ArrayOperations.SetLength(obj, length - actualDeleteCount + itemCount);
        return array;
    }

    /// <summary>The steps of splice that move the elements after the deleted ones, so that there is room for the inserted items.</summary>
    private static void MoveElements(JsObject obj, long length, long actualStart, long actualDeleteCount, long itemCount)
    {
        if (itemCount == actualDeleteCount)
            return;

        // Fast path: an array without holes, extensible, whose prototypes have no elements
        if (length + itemCount < int.MaxValue && ArrayOperations.IsPackedArray(obj, length, out var packed))
        {
            var newLength = length - actualDeleteCount + itemCount;
            if (itemCount > actualDeleteCount)
            {
                // Grow the storage, then move the tail to the right
                packed.SetOwnSlot(new PropertyKey((uint)(newLength - 1)), JsValue.Undefined, PropertyFlags.Default);
                ArrayOperations.SetLength(packed, newLength);
                var elements = packed.DenseElements!;
                var tail = elements.AsSpan((int)(actualStart + actualDeleteCount), (int)(length - actualStart - actualDeleteCount));
                tail.CopyTo(elements.AsSpan((int)(actualStart + itemCount)));
                elements.AsSpan((int)actualStart, (int)itemCount).Fill(JsValue.Undefined);
            }
            else
            {
                var elements = packed.DenseElements!;
                var tail = elements.AsSpan((int)(actualStart + actualDeleteCount), (int)(length - actualStart - actualDeleteCount));
                tail.CopyTo(elements.AsSpan((int)(actualStart + itemCount)));
                ArrayOperations.SetLength(packed, newLength);
            }

            return;
        }

        if (itemCount < actualDeleteCount)
        {
            var end = length - actualDeleteCount;
            for (var k = actualStart; k < end; k++)
            {
                // Skip the indices whose source and target are both holes (when that is not observable)
                var nextSource = ArrayOperations.SkipHoles(obj, k + actualDeleteCount, length) - actualDeleteCount;
                var nextTarget = ArrayOperations.SkipHoles(obj, k + itemCount, end + itemCount) - itemCount;
                k = Math.Min(nextSource, nextTarget);
                if (k >= end)
                    break;

                if (ArrayOperations.TryGetElement(obj, k + actualDeleteCount, out var fromValue))
                {
                    ArrayOperations.SetElement(obj, k + itemCount, fromValue);
                }
                else
                {
                    ArrayOperations.DeleteElement(obj, k + itemCount);
                }
            }

            var newLength = length - actualDeleteCount + itemCount;
            for (var k = length; k > newLength; k--)
            {
                // Deleting a hole is not observable when reading it is not
                k = ArrayOperations.SkipHolesBackward(obj, k - 1) + 1;
                if (k <= newLength)
                    break;

                ArrayOperations.DeleteElement(obj, k - 1);
            }
        }
        else
        {
            for (var k = length - actualDeleteCount; k > actualStart; k--)
            {
                var previousSource = ArrayOperations.SkipHolesBackward(obj, k + actualDeleteCount - 1) - actualDeleteCount + 1;
                var previousTarget = ArrayOperations.SkipHolesBackward(obj, k + itemCount - 1) - itemCount + 1;
                k = Math.Max(previousSource, previousTarget);
                if (k <= actualStart)
                    break;

                if (ArrayOperations.TryGetElement(obj, k + actualDeleteCount - 1, out var fromValue))
                {
                    ArrayOperations.SetElement(obj, k + itemCount - 1, fromValue);
                }
                else
                {
                    ArrayOperations.DeleteElement(obj, k + itemCount - 1);
                }
            }
        }
    }

    /// <summary>Array.prototype.toLocaleString([reserved1 [, reserved2]])</summary>
    private static JsValue ToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var array = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(array);

        var result = new StringBuilder();
        for (long k = 0; k < length; k++)
        {
            if (k > 0)
            {
                // The implementation-defined list-separator String
                StringOperations.CheckLength((long)result.Length + 1);
                result.Append(',');
            }

            var nextElement = ArrayOperations.GetElement(array, k);
            if (!nextElement.IsNullOrUndefined)
            {
                var s = JsOperations.ToString(JsOperations.Invoke(nextElement, ToLocaleStringKey, []));
                StringOperations.CheckLength((long)result.Length + s.Length);
                result.Append(s);
            }
        }

        return result.ToString();
    }

    /// <summary>Array.prototype.toReversed()</summary>
    private static JsValue ToReversed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var array = ArrayOperations.ArrayCreate(length);
        for (long k = 0; k < length; k++)
        {
            var fromValue = ArrayOperations.GetElement(obj, length - k - 1);
            array.SetOwnSlot(new PropertyKey((uint)k), fromValue, PropertyFlags.Default);
        }

        return array;
    }

    /// <summary>Array.prototype.toSorted(comparator)</summary>
    private static JsValue ToSorted(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var comparer = GetComparer(Arg(arguments, 0), "toSorted");
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var array = ArrayOperations.ArrayCreate(length);
        var sortedList = ArrayOperations.SortIndexedProperties(obj, length, comparer, skipHoles: false);
        for (var j = 0; j < sortedList.Count; j++)
        {
            array.SetOwnSlot(new PropertyKey((uint)j), sortedList[j], PropertyFlags.Default);
        }

        return array;
    }

    /// <summary>Array.prototype.toSpliced(start, skipCount, ...items)</summary>
    private static JsValue ToSpliced(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var actualStart = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var items = arguments.Length > 2 ? arguments[2..] : ReadOnlySpan<JsValue>.Empty;
        long actualSkipCount;
        if (arguments.Length == 0)
        {
            actualSkipCount = 0;
        }
        else if (arguments.Length == 1)
        {
            actualSkipCount = length - actualStart;
        }
        else
        {
            var skipCount = JsOperations.ToIntegerOrInfinity(arguments[1]);
            actualSkipCount = (long)Math.Clamp(skipCount, 0, length - actualStart);
        }

        var newLength = length + items.Length - actualSkipCount;
        if (newLength > MaxSafeInteger)
        {
            ThrowTooLong();
        }

        var array = ArrayOperations.ArrayCreate(newLength);
        long i = 0;
        var r = actualStart + actualSkipCount;
        for (; i < actualStart; i++)
        {
            array.SetOwnSlot(new PropertyKey((uint)i), ArrayOperations.GetElement(obj, i), PropertyFlags.Default);
        }

        foreach (var item in items)
        {
            array.SetOwnSlot(new PropertyKey((uint)i), item, PropertyFlags.Default);
            i++;
        }

        for (; i < newLength; i++, r++)
        {
            array.SetOwnSlot(new PropertyKey((uint)i), ArrayOperations.GetElement(obj, r), PropertyFlags.Default);
        }

        return array;
    }

    /// <summary>Array.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var array = JsOperations.ToObject(thisValue);
        var join = array.Get(JoinKey);
        if (join.Cell is JsObject { IsCallable: true } joinFunction)
            return joinFunction.Call(array, []);

        if (function.State is JsObject objectToString)
            return objectToString.Call(array, []);

        return ObjectToStringFallback(array);
    }

    /// <summary>Object.prototype.toString when %Object.prototype.toString% does not exist (the Object built-ins are not initialized).</summary>
    private static JsValue ObjectToStringFallback(JsObject obj)
    {
        var builtinTag = obj switch
        {
            _ when JsOperations.IsArray(obj) => "Array",
            { IsCallable: true } => "Function",
            { Class: ObjectClass.Arguments } => "Arguments",
            { Class: ObjectClass.Error } => "Error",
            { Class: ObjectClass.Boolean } => "Boolean",
            { Class: ObjectClass.Number } => "Number",
            { Class: ObjectClass.String } => "String",
            { Class: ObjectClass.Date } => "Date",
            { Class: ObjectClass.RegExp } => "RegExp",
            _ => "Object",
        };

        var tag = obj.Get(new PropertyKey(WellKnownSymbols.ToStringTag));
        return "[object " + (tag.Cell is JsString str ? str.ToString() : builtinTag) + "]";
    }

    /// <summary>Array.prototype.unshift(...items)</summary>
    private static JsValue Unshift(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var argCount = arguments.Length;
        if (argCount > 0)
        {
            if (length + argCount > MaxSafeInteger)
            {
                ThrowTooLong();
            }

            MoveElements(obj, length, actualStart: 0, actualDeleteCount: 0, itemCount: argCount);

            for (var j = 0; j < argCount; j++)
            {
                ArrayOperations.SetElement(obj, j, arguments[j]);
            }
        }

        ArrayOperations.SetLength(obj, length + argCount);
        return length + argCount;
    }

    /// <summary>Array.prototype.with(index, value)</summary>
    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(obj);
        var relativeIndex = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        var actualIndex = relativeIndex >= 0 ? relativeIndex : length + relativeIndex;
        if (actualIndex >= length || actualIndex < 0)
            return ThrowHelper.ThrowRangeError<JsValue>("Invalid index : " + NumberFormatting.ToString(relativeIndex));

        var value = Arg(arguments, 1);
        var array = ArrayOperations.ArrayCreate(length);
        for (long k = 0; k < length; k++)
        {
            var fromValue = k == (long)actualIndex ? value : ArrayOperations.GetElement(obj, k);
            array.SetOwnSlot(new PropertyKey((uint)k), fromValue, PropertyFlags.Default);
        }

        return array;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // %ArrayIteratorPrototype%
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>%ArrayIteratorPrototype%.next()</summary>
    private static JsValue IteratorNext(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var hasValue = IteratorStep(function, thisValue, out var value);
        return IteratorOperations.CreateIteratorResultObject(function.Realm, value, done: !hasValue);
    }

    /// <summary>The step of %ArrayIteratorPrototype%.next(): false when the iterator is done.</summary>
    private static bool IteratorStep(JsBuiltinFunction function, JsValue thisValue, out JsValue value)
    {
        if (thisValue.Cell is not JsArrayIterator iterator)
        {
            ThrowHelper.ThrowTypeError("%ArrayIteratorPrototype%.next requires that 'this' be an Array Iterator");
            value = default;
            return false;
        }

        value = default;
        if (iterator.Iterated is not { } array)
            return false;

        if (iterator.IsExecuting)
        {
            ThrowHelper.ThrowTypeError("Generator is already running");
            return false;
        }

        iterator.IsExecuting = true;
        var completed = true;
        try
        {
            long length;
            if (array is JsArray jsArray)
            {
                length = jsArray.Length;
            }
            else
            {
                length = array is IArrayIteratorSource source ? source.GetIteratorLength() : JsOperations.LengthOfArrayLike(array);
            }

            var index = iterator.NextIndex;
            if (index >= length)
                return false;

            if (iterator.IterationKind == ArrayIteratorKind.Keys)
            {
                value = index;
            }
            else
            {
                var elementValue = ArrayOperations.GetElement(array, index);
                value = iterator.IterationKind == ArrayIteratorKind.Values ? elementValue : JsArray.Create(function.Realm, [index, elementValue]);
            }

            iterator.NextIndex = index + 1;
            completed = false;
            return true;
        }
        finally
        {
            iterator.IsExecuting = false;
            if (completed)
            {
                iterator.Iterated = null;
            }
        }
    }
}
