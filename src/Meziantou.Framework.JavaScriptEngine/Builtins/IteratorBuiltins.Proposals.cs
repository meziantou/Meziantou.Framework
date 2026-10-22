namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The finished iterator proposals: joint iteration (Iterator.zip, Iterator.zipKeyed), iterator chunking
/// (Iterator.prototype.chunks, windows) and Iterator.prototype.includes.
/// </summary>
internal static partial class IteratorBuiltins
{
    private const double MaxUint32 = 4294967295;

    private static readonly PropertyKey ModeKey = new("mode");
    private static readonly PropertyKey PaddingKey = new("padding");

    private static readonly BuiltinMethodTable ProposalConstructorMethods =
    [
        new("zip", 1, Zip),
        new("zipKeyed", 1, ZipKeyed),
    ];

    private static readonly BuiltinMethodTable ProposalPrototypeMethods =
    [
        new("chunks", 1, Chunks),
        new("includes", 1, Includes),
        new("windows", 1, Windows),
    ];

    private static void InitializeProposals(JsRealm realm, JsFunction constructor, JsObject prototype)
    {
        realm.DefineMethods(constructor, ProposalConstructorMethods);
        realm.DefineMethods(prototype, ProposalPrototypeMethods);
    }

    /// <summary>IteratorCloseAll(iters, completion) for a normal or return completion: all the iterators are closed, in reverse order, then the first error is thrown.</summary>
    internal static void IteratorCloseAll(List<IteratorRecord> iterators)
    {
        JsException? exception = null;
        for (var i = iterators.Count - 1; i >= 0; i--)
        {
            if (exception is not null)
            {
                IteratorOperations.IteratorCloseOnThrow(iterators[i]);
                continue;
            }

            try
            {
                IteratorOperations.IteratorClose(iterators[i]);
            }
            catch (JsException ex)
            {
                exception = ex;
            }
        }

        if (exception is not null)
            throw exception;
    }

    /// <summary>IteratorCloseAll(iters, completion) for a throw completion: the errors of the return methods are ignored (the caller rethrows its exception).</summary>
    internal static void IteratorCloseAllOnThrow(List<IteratorRecord> iterators)
    {
        for (var i = iterators.Count - 1; i >= 0; i--)
        {
            IteratorOperations.IteratorCloseOnThrow(iterators[i]);
        }
    }

    /// <summary>IteratorCloseAll(iters, ThrowCompletion(a newly created TypeError object))</summary>
    internal static void IteratorCloseAllAndThrowTypeError(List<IteratorRecord> iterators, string message)
    {
        var exception = ThrowHelper.CreateException(ErrorType.TypeError, message);
        IteratorCloseAllOnThrow(iterators);
        throw exception;
    }

    /// <summary>Reads the options of Iterator.zip and Iterator.zipKeyed: the mode, and the padding option of the longest mode.</summary>
    private static (ZipMode Mode, JsValue PaddingOption) GetZipOptions(JsValue optionsArgument)
    {
        // GetOptionsObject(options)
        JsObject? options;
        if (optionsArgument.IsUndefined)
        {
            options = null;
        }
        else
        {
            options = optionsArgument.Cell as JsObject;
            if (options is null)
                return ThrowHelper.ThrowTypeError<(ZipMode, JsValue)>("The options of Iterator.zip must be an object");
        }

        var modeValue = options is null ? JsValue.Undefined : options.Get(ModeKey);
        var mode = modeValue.IsUndefined ? ZipMode.Shortest : modeValue.Cell is JsString modeString ? modeString.ToString() switch
        {
            "shortest" => ZipMode.Shortest,
            "longest" => ZipMode.Longest,
            "strict" => ZipMode.Strict,
            _ => (ZipMode)(-1),
        } : (ZipMode)(-1);

        if (mode is < ZipMode.Shortest or > ZipMode.Strict)
            return ThrowHelper.ThrowTypeError<(ZipMode, JsValue)>("The mode of Iterator.zip must be \"shortest\", \"longest\" or \"strict\"");

        var paddingOption = JsValue.Undefined;
        if (mode == ZipMode.Longest && options is not null)
        {
            paddingOption = options.Get(PaddingKey);
            if (!paddingOption.IsUndefined && !paddingOption.IsObject)
                return ThrowHelper.ThrowTypeError<(ZipMode, JsValue)>("The padding of Iterator.zip must be an object");
        }

        return (mode, paddingOption);
    }

    /// <summary>Iterator.zip(iterables [, options])</summary>
    private static JsValue Zip(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var iterables = Arg(arguments, 0);
        if (!iterables.IsObject)
            return ThrowHelper.ThrowTypeError<JsValue>("Iterator.zip: the iterables must be an object");

        var (mode, paddingOption) = GetZipOptions(Arg(arguments, 1));
        var iters = new List<IteratorRecord>();
        var inputIter = IteratorOperations.GetIterator(iterables);
        while (true)
        {
            JsValue next;
            try
            {
                if (!IteratorOperations.IteratorStepValue(inputIter, out next))
                    break;
            }
            catch (JsException)
            {
                IteratorCloseAllOnThrow(iters);
                throw;
            }

            try
            {
                iters.Add(GetIteratorFlattenable(next, iterateStrings: false));
            }
            catch (JsException)
            {
                IteratorCloseAllOnThrow(iters);
                IteratorOperations.IteratorCloseOnThrow(inputIter);
                throw;
            }
        }

        var padding = new JsValue[iters.Count];
        if (mode == ZipMode.Longest && !paddingOption.IsUndefined)
        {
            IteratorRecord paddingIter;
            try
            {
                paddingIter = IteratorOperations.GetIterator(paddingOption);
            }
            catch (JsException)
            {
                IteratorCloseAllOnThrow(iters);
                throw;
            }

            var usingIterator = true;
            for (var i = 0; i < padding.Length && usingIterator; i++)
            {
                try
                {
                    usingIterator = IteratorOperations.IteratorStepValue(paddingIter, out padding[i]);
                }
                catch (JsException)
                {
                    IteratorCloseAllOnThrow(iters);
                    throw;
                }
            }

            if (usingIterator)
            {
                try
                {
                    IteratorOperations.IteratorClose(paddingIter);
                }
                catch (JsException)
                {
                    IteratorCloseAllOnThrow(iters);
                    throw;
                }
            }
        }

        return new JsZipIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iters, mode, padding, keys: null);
    }

    /// <summary>Iterator.zipKeyed(iterables [, options])</summary>
    private static JsValue ZipKeyed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (Arg(arguments, 0).Cell is not JsObject iterables)
            return ThrowHelper.ThrowTypeError<JsValue>("Iterator.zipKeyed: the iterables must be an object");

        var (mode, paddingOption) = GetZipOptions(Arg(arguments, 1));
        var iters = new List<IteratorRecord>();
        var keys = new List<PropertyKey>();
        var allKeys = iterables.OwnPropertyKeys();
        foreach (var key in allKeys)
        {
            try
            {
                if (!iterables.GetOwnProperty(key, out var descriptor) || !descriptor.Enumerable)
                    continue;

                var value = iterables.Get(key);
                if (value.IsUndefined)
                    continue;

                keys.Add(key);
                iters.Add(GetIteratorFlattenable(value, iterateStrings: false));
            }
            catch (JsException)
            {
                IteratorCloseAllOnThrow(iters);
                throw;
            }
        }

        var padding = new JsValue[iters.Count];
        if (mode == ZipMode.Longest && paddingOption.Cell is JsObject paddingObject)
        {
            for (var i = 0; i < padding.Length; i++)
            {
                try
                {
                    padding[i] = paddingObject.Get(keys[i]);
                }
                catch (JsException)
                {
                    IteratorCloseAllOnThrow(iters);
                    throw;
                }
            }
        }

        return new JsZipIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iters, mode, padding, [.. keys]);
    }

    private static bool IsIntegralNumber(JsValue value) => value.IsNumber && double.IsFinite(value.NumberValue) && Math.Truncate(value.NumberValue) == value.NumberValue;

    /// <summary>The validation of the size of chunks and windows: a Number, integral, in [1, 2^32-1]; an error closes the iterator.</summary>
    private static long GetSizeArgument(JsObject iterator, JsValue size, string methodName)
    {
        if (!IsIntegralNumber(size))
            return CloseAndThrow<long>(iterator, ErrorType.TypeError, $"Iterator.prototype.{methodName}: the size must be an integral number");

        var number = size.NumberValue;
        if (number is < 1 or > MaxUint32)
            return CloseAndThrow<long>(iterator, ErrorType.RangeError, $"Iterator.prototype.{methodName}: the size must be between 1 and 2^32-1");

        return (long)number;
    }

    /// <summary>Iterator.prototype.chunks(chunkSize)</summary>
    private static JsValue Chunks(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "chunks");
        var chunkSize = GetSizeArgument(obj, Arg(arguments, 0), "chunks");
        var iterated = IteratorOperations.GetIteratorDirect(obj);
        return new JsChunksIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterated, chunkSize);
    }

    /// <summary>Iterator.prototype.windows(windowSize [, undersized])</summary>
    private static JsValue Windows(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "windows");
        var windowSize = GetSizeArgument(obj, Arg(arguments, 0), "windows");
        var undersized = Arg(arguments, 1);
        bool allowPartial;
        if (undersized.IsUndefined)
        {
            allowPartial = false;
        }
        else
        {
            var text = undersized.Cell is JsString str ? str.ToString() : null;
            if (text is not ("only-full" or "allow-partial"))
                return CloseAndThrow<JsValue>(obj, ErrorType.TypeError, "Iterator.prototype.windows: undersized must be \"only-full\" or \"allow-partial\"");

            allowPartial = text == "allow-partial";
        }

        var iterated = IteratorOperations.GetIteratorDirect(obj);
        return new JsWindowsIteratorHelper(function.Realm.Intrinsics.IteratorHelperPrototype, iterated, windowSize, allowPartial);
    }

    /// <summary>Iterator.prototype.includes(searchElement [, skippedElements])</summary>
    private static JsValue Includes(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var obj = ThisObject(thisValue, "includes");
        var searchElement = Arg(arguments, 0);
        var skippedElements = Arg(arguments, 1);
        double toSkip;
        if (skippedElements.IsUndefined)
        {
            toSkip = 0;
        }
        else
        {
            if (!IsIntegralNumber(skippedElements) && !(skippedElements.IsNumber && double.IsInfinity(skippedElements.NumberValue)))
                return CloseAndThrow<JsValue>(obj, ErrorType.TypeError, "Iterator.prototype.includes: skippedElements must be an integral number");

            toSkip = skippedElements.NumberValue;
            if (toSkip < 0 || (double.IsFinite(toSkip) && toSkip > MaxSafeInteger))
                return CloseAndThrow<JsValue>(obj, ErrorType.RangeError, "Iterator.prototype.includes: skippedElements must be between 0 and 2^53-1");
        }

        var iterated = IteratorOperations.GetIteratorDirect(obj);
        double skipped = 0;
        while (IteratorOperations.IteratorStepValue(iterated, out var value))
        {
            if (skipped < toSkip)
            {
                skipped++;
            }
            else if (JsOperations.SameValueZero(value, searchElement))
            {
                IteratorOperations.IteratorClose(iterated);
                return true;
            }
        }

        return false;
    }
}
