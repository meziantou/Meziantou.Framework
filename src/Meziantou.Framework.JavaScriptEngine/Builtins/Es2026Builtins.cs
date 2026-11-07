namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The ES2026 methods added to the earlier built-ins: Error.isError and Array.fromAsync (https://tc39.es/ecma262/#sec-array.fromasync).</summary>
internal static class Es2026Builtins
{
    /// <summary>Defines Error.isError; creates the Array.fromAsync function of the realm (it is defined on %Array% by the caller).</summary>
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        realm.DefineMethod(intrinsics.GetErrorConstructor(ErrorType.Error), "isError", 1, IsError);
        return realm.CreateBuiltinFunction("fromAsync", 1, FromAsync);
    }

    /// <summary>Error.isError(arg): true for the objects that have an [[ErrorData]] internal slot (not the proxies).</summary>
    private static JsValue IsError(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return arguments.Length > 0 && arguments[0].Cell is JsObject { Class: ObjectClass.Error };
    }

    /// <summary>Array.fromAsync(asyncItems [, mapper [, thisArg]])</summary>
    private static JsValue FromAsync(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var realm = function.Realm;
        var promiseCapability = PromiseCapability.CreateIntrinsic(realm);
        var operation = new ArrayFromAsyncOperation(
            realm,
            promiseCapability,
            constructor: thisValue,
            asyncItems: arguments.Length > 0 ? arguments[0] : default,
            mapper: arguments.Length > 1 ? arguments[1] : default,
            thisArg: arguments.Length > 2 ? arguments[2] : default);

        // AsyncFunctionStart(promiseCapability, fromAsyncClosure)
        operation.Start();
        return promiseCapability.Promise;
    }
}

/// <summary>
/// The fromAsyncClosure of Array.fromAsync, run as an async function: a state machine resumed by promise continuations
/// (no function object is created for the Awaits). A synchronous iterable is consumed through the steps of
/// CreateAsyncFromSyncIterator (%AsyncFromSyncIteratorPrototype%.next and return), which are not observable as objects.
/// </summary>
internal sealed class ArrayFromAsyncOperation
{
    private const long MaxSafeInteger = 9007199254740991;

    private static readonly PropertyKey LengthKey = new("length");
    private static readonly PropertyKey ReturnKey = new("return");
    private static readonly PromiseContinuation OnAwaitFulfilled = static (state, value) => ((ArrayFromAsyncOperation)state!).Resume(value, thrown: null);
    private static readonly PromiseContinuation OnAwaitRejected = static (state, reason) => ((ArrayFromAsyncOperation)state!).Resume(default, reason);

    private readonly JsRealm _realm;
    private readonly PromiseCapability _promiseCapability;
    private readonly JsValue _constructor;
    private readonly JsValue _asyncItems;
    private readonly JsValue _mapper;
    private readonly JsValue _thisArg;
    private bool _mapping;

    // The iterator of an async iterable, or the synchronous iterator wrapped by CreateAsyncFromSyncIterator
    private IteratorRecord? _iterator;
    private bool _isSyncIterator;

    private JsObject? _arrayLike;
    private long _length;

    private JsObject _result = null!;
    private long _index;
    private ResumePoint _resumePoint;
    private JsValue _closeError;

    public ArrayFromAsyncOperation(JsRealm realm, PromiseCapability promiseCapability, JsValue constructor, JsValue asyncItems, JsValue mapper, JsValue thisArg)
    {
        _realm = realm;
        _promiseCapability = promiseCapability;
        _constructor = constructor;
        _asyncItems = asyncItems;
        _mapper = mapper;
        _thisArg = thisArg;
    }

    private enum ResumePoint : byte
    {
        IteratorNextResult,
        IteratorMappedValue,
        IteratorClosed,
        ArrayLikeValue,
        ArrayLikeMappedValue,
    }

    /// <summary>Runs the steps of the closure until the first Await.</summary>
    public void Start()
    {
        try
        {
            if (Initialize())
            {
                IteratorLoop();
            }
            else
            {
                ArrayLikeLoop();
            }
        }
        catch (JsException exception)
        {
            _promiseCapability.Reject(exception.Value);
        }
    }

    /// <summary>The steps before the loops: returns true for an iterable, false for an array-like.</summary>
    private bool Initialize()
    {
        if (!_mapper.IsUndefined)
        {
            if (!_mapper.IsCallable)
                return ThrowHelper.ThrowTypeError<bool>($"Array.fromAsync: {JsOperations.DescribeForError(_mapper)} is not a function");

            _mapping = true;
        }

        var usingAsyncIterator = JsOperations.GetMethod(_asyncItems, new PropertyKey(WellKnownSymbols.AsyncIterator));
        JsObject? usingSyncIterator = null;
        if (usingAsyncIterator is null)
        {
            usingSyncIterator = JsOperations.GetMethod(_asyncItems, new PropertyKey(WellKnownSymbols.Iterator));
        }

        if (usingAsyncIterator is not null)
        {
            _iterator = IteratorOperations.GetIteratorFromMethod(_asyncItems, usingAsyncIterator);
        }
        else if (usingSyncIterator is not null)
        {
            _iterator = IteratorOperations.GetIteratorFromMethod(_asyncItems, usingSyncIterator);
            _isSyncIterator = true;
        }

        if (_iterator is not null)
        {
            _result = _constructor.Cell is JsObject { IsConstructor: true } constructor
                ? JsOperations.Construct(constructor, []).AsObject()
                : new JsArray(_realm.Intrinsics.ArrayPrototype);
            return true;
        }

        // asyncItems is neither an AsyncIterable nor an Iterable so assume it is an array-like object
        _arrayLike = JsOperations.ToObject(_asyncItems);
        _length = JsOperations.LengthOfArrayLike(_arrayLike);
        if (_constructor.Cell is JsObject { IsConstructor: true } arrayLikeConstructor)
        {
            _result = JsOperations.Construct(arrayLikeConstructor, [(double)_length]).AsObject();
        }
        else
        {
            // ArrayCreate(len)
            if (_length > uint.MaxValue)
                return ThrowHelper.ThrowRangeError<bool>("Invalid array length");

            _result = new JsArray(_realm.Intrinsics.ArrayPrototype, (uint)_length);
        }

        return false;
    }

    /// <summary>Resumes the closure after an Await.</summary>
    private void Resume(JsValue value, JsValue? thrown)
    {
        var engine = _realm.Engine;
        var previousRealm = engine.CurrentRealm;
        engine.CurrentRealm = _realm;
        try
        {
            switch (_resumePoint)
            {
                case ResumePoint.IteratorNextResult:
                    if (thrown is { } nextError)
                        throw new JsException(nextError);

                    if (OnNextResult(value))
                    {
                        IteratorLoop();
                    }

                    break;

                case ResumePoint.IteratorMappedValue:
                    if (thrown is { } mapError)
                    {
                        CloseIterator(mapError);
                    }
                    else if (DefineIteratorValue(value))
                    {
                        IteratorLoop();
                    }

                    break;

                case ResumePoint.IteratorClosed:
                    // AsyncIteratorClose with a throw completion: the result of return() is ignored
                    _promiseCapability.Reject(_closeError);
                    break;

                case ResumePoint.ArrayLikeValue:
                    if (thrown is { } valueError)
                        throw new JsException(valueError);

                    if (OnArrayLikeValue(value))
                    {
                        ArrayLikeLoop();
                    }

                    break;

                case ResumePoint.ArrayLikeMappedValue:
                    if (thrown is { } mappedError)
                        throw new JsException(mappedError);

                    DefineArrayLikeValue(value);
                    ArrayLikeLoop();
                    break;
            }
        }
        catch (JsException exception)
        {
            _promiseCapability.Reject(exception.Value);
        }
        finally
        {
            engine.CurrentRealm = previousRealm;
        }
    }

    /// <summary>
    /// Await(value): registers the continuation and returns. An exception thrown by PromiseResolve (a throw completion of
    /// the Await) is thrown to the caller.
    /// </summary>
    private void Await(JsValue value, ResumePoint resumePoint)
    {
        var promise = JsPromise.PromiseResolve(_realm, value);
        _resumePoint = resumePoint;
        JsPromise.PerformPromiseThen(promise, OnAwaitFulfilled, OnAwaitRejected, this);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Iterables
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The first steps of an iteration: calls next() and awaits its result.</summary>
    private void IteratorLoop()
    {
        if (_index >= MaxSafeInteger)
        {
            CloseIterator(ThrowHelper.CreateException(ErrorType.TypeError, "Array.fromAsync: too many elements").Value);
            return;
        }

        var nextResult = _isSyncIterator ? AsyncFromSyncIteratorNext() : JsOperations.Call(_iterator!.NextMethod, _iterator.Iterator, []);
        Await(nextResult, ResumePoint.IteratorNextResult);
    }

    /// <summary>The steps after the Await of the result of next(): returns true when the next iteration must start.</summary>
    private bool OnNextResult(JsValue nextResult)
    {
        if (nextResult.Cell is not JsObject nextResultObject)
            return ThrowHelper.ThrowTypeError<bool>($"Iterator result {JsOperations.DescribeForError(nextResult)} is not an object");

        if (IteratorOperations.IteratorComplete(nextResultObject))
        {
            _result.Set(LengthKey, (double)_index, throwOnFailure: true);
            _promiseCapability.Resolve(_result);
            return false;
        }

        var nextValue = IteratorOperations.IteratorValue(nextResultObject);
        if (!_mapping)
            return DefineIteratorValue(nextValue);

        try
        {
            var mappedValue = JsOperations.Call(_mapper, _thisArg, [nextValue, (double)_index]);
            Await(mappedValue, ResumePoint.IteratorMappedValue);
        }
        catch (JsException exception)
        {
            // IfAbruptCloseAsyncIterator
            CloseIterator(exception.Value);
        }

        return false;
    }

    /// <summary>CreateDataPropertyOrThrow(A, Pk, mappedValue), closing the iterator when it fails: returns true when the next iteration must start.</summary>
    private bool DefineIteratorValue(JsValue value)
    {
        try
        {
            _result.CreateDataPropertyOrThrow(PropertyKey.FromInteger(_index), value);
        }
        catch (JsException exception)
        {
            CloseIterator(exception.Value);
            return false;
        }

        _index++;
        return true;
    }

    /// <summary>AsyncIteratorClose(iteratorRecord, ThrowCompletion(error)): the promise is rejected with <paramref name="error"/>.</summary>
    private void CloseIterator(JsValue error)
    {
        _closeError = error;
        JsValue innerResult;
        if (_isSyncIterator)
        {
            // GetMethod(asyncFromSyncIterator, "return") is %AsyncFromSyncIteratorPrototype%.return, which never throws
            innerResult = AsyncFromSyncIteratorReturn();
        }
        else
        {
            try
            {
                var returnMethod = JsOperations.GetMethod(_iterator!.Iterator, ReturnKey);
                if (returnMethod is null)
                {
                    _promiseCapability.Reject(error);
                    return;
                }

                innerResult = returnMethod.Call(_iterator.Iterator, []);
            }
            catch (JsException)
            {
                _promiseCapability.Reject(error);
                return;
            }
        }

        try
        {
            Await(innerResult, ResumePoint.IteratorClosed);
        }
        catch (JsException)
        {
            _promiseCapability.Reject(error);
        }
    }

    /// <summary>%AsyncFromSyncIteratorPrototype%.next() on the synchronous iterator.</summary>
    private JsPromise AsyncFromSyncIteratorNext()
    {
        var capability = PromiseCapability.CreateIntrinsic(_realm);
        JsObject result;
        try
        {
            result = IteratorOperations.IteratorNext(_iterator!);
        }
        catch (JsException exception)
        {
            capability.Reject(exception.Value);
            return (JsPromise)capability.Promise;
        }

        AsyncFromSyncIteratorContinuation(result, capability, closeOnRejection: true);
        return (JsPromise)capability.Promise;
    }

    /// <summary>%AsyncFromSyncIteratorPrototype%.return() on the synchronous iterator.</summary>
    private JsPromise AsyncFromSyncIteratorReturn()
    {
        var capability = PromiseCapability.CreateIntrinsic(_realm);
        var syncIterator = _iterator!.Iterator;
        JsValue result;
        try
        {
            var returnMethod = JsOperations.GetMethod(syncIterator, ReturnKey);
            if (returnMethod is null)
            {
                capability.Resolve(IteratorOperations.CreateIteratorResultObject(_realm, JsValue.Undefined, done: true));
                return (JsPromise)capability.Promise;
            }

            result = returnMethod.Call(syncIterator, []);
        }
        catch (JsException exception)
        {
            capability.Reject(exception.Value);
            return (JsPromise)capability.Promise;
        }

        if (result.Cell is not JsObject resultObject)
        {
            capability.Reject(ThrowHelper.CreateException(ErrorType.TypeError, "Iterator result is not an object").Value);
            return (JsPromise)capability.Promise;
        }

        AsyncFromSyncIteratorContinuation(resultObject, capability, closeOnRejection: false);
        return (JsPromise)capability.Promise;
    }

    /// <summary>AsyncFromSyncIteratorContinuation(result, promiseCapability, syncIteratorRecord, closeOnRejection)</summary>
    private void AsyncFromSyncIteratorContinuation(JsObject result, PromiseCapability capability, bool closeOnRejection)
    {
        bool done;
        JsValue value;
        try
        {
            done = IteratorOperations.IteratorComplete(result);
            value = IteratorOperations.IteratorValue(result);
        }
        catch (JsException exception)
        {
            capability.Reject(exception.Value);
            return;
        }

        JsPromise valueWrapper;
        try
        {
            valueWrapper = JsPromise.PromiseResolve(_realm, value);
        }
        catch (JsException exception)
        {
            if (!done && closeOnRejection)
            {
                IteratorOperations.IteratorCloseOnThrow(_iterator!);
            }

            capability.Reject(exception.Value);
            return;
        }

        var state = new AsyncFromSyncContinuationState(this, capability, done, closeOnRejection: !done && closeOnRejection);
        JsPromise.PerformPromiseThen(valueWrapper, AsyncFromSyncContinuationState.OnFulfilled, AsyncFromSyncContinuationState.OnRejected, state);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Array-likes
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The first steps of an iteration on an array-like: Get(arrayLike, Pk) and Await; at the end, sets the length and resolves the promise.</summary>
    private void ArrayLikeLoop()
    {
        if (_index < _length)
        {
            var value = _arrayLike!.Get(PropertyKey.FromInteger(_index));
            Await(value, ResumePoint.ArrayLikeValue);
            return;
        }

        _result.Set(LengthKey, (double)_length, throwOnFailure: true);
        _promiseCapability.Resolve(_result);
    }

    /// <summary>The steps after the Await of an element: returns true when the next iteration must start.</summary>
    private bool OnArrayLikeValue(JsValue value)
    {
        if (_mapping)
        {
            var mappedValue = JsOperations.Call(_mapper, _thisArg, [value, (double)_index]);
            Await(mappedValue, ResumePoint.ArrayLikeMappedValue);
            return false;
        }

        DefineArrayLikeValue(value);
        return true;
    }

    private void DefineArrayLikeValue(JsValue value)
    {
        _result.CreateDataPropertyOrThrow(PropertyKey.FromInteger(_index), value);
        _index++;
    }

    /// <summary>The state of the handlers of AsyncFromSyncIteratorContinuation (unwrap and closeIterator).</summary>
    private sealed class AsyncFromSyncContinuationState(ArrayFromAsyncOperation operation, PromiseCapability capability, bool done, bool closeOnRejection)
    {
        public static readonly PromiseContinuation OnFulfilled = static (state, value) => ((AsyncFromSyncContinuationState)state!).Fulfilled(value);
        public static readonly PromiseContinuation OnRejected = static (state, reason) => ((AsyncFromSyncContinuationState)state!).Rejected(reason);

        private void Fulfilled(JsValue value)
        {
            var realm = operation._realm;
            var engine = realm.Engine;
            var previousRealm = engine.CurrentRealm;
            engine.CurrentRealm = realm;
            try
            {
                capability.Resolve(IteratorOperations.CreateIteratorResultObject(realm, value, done));
            }
            finally
            {
                engine.CurrentRealm = previousRealm;
            }
        }

        private void Rejected(JsValue reason)
        {
            var realm = operation._realm;
            var engine = realm.Engine;
            var previousRealm = engine.CurrentRealm;
            engine.CurrentRealm = realm;
            try
            {
                if (closeOnRejection)
                {
                    // closeIterator: IteratorClose(syncIteratorRecord, ThrowCompletion(error))
                    IteratorOperations.IteratorCloseOnThrow(operation._iterator!);
                }

                capability.Reject(reason);
            }
            finally
            {
                engine.CurrentRealm = previousRealm;
            }
        }
    }
}
