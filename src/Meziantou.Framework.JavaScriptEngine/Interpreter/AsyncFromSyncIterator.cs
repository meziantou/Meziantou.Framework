namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>%AsyncFromSyncIteratorPrototype% (https://tc39.es/ecma262/#sec-%asyncfromsynciteratorprototype%-object).</summary>
internal sealed class AsyncFromSyncIterator(JsObject prototype, IteratorRecord syncIteratorRecord) : JsObject(prototype, ObjectClass.AsyncFromSyncIterator)
{
    public IteratorRecord SyncIteratorRecord { get; } = syncIteratorRecord;

    /// <summary>CreateAsyncFromSyncIterator(syncIteratorRecord)</summary>
    public static IteratorRecord Create(JsRealm realm, IteratorRecord syncIteratorRecord)
    {
        var iterator = new AsyncFromSyncIterator(realm.Intrinsics.AsyncFromSyncIteratorPrototype, syncIteratorRecord);
        var next = iterator.Get(new PropertyKey("next"));
        return new IteratorRecord(iterator, next);
    }

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("next", 1, Next),
        new("return", 1, Return),
        new("throw", 1, Throw),
    ];

    public static void Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        realm.DefineMethods(prototype, PrototypeMethods);
    }

    private static JsValue Next(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var iterator = (AsyncFromSyncIterator)thisValue.Cell!;
        var capability = PromiseCapability.CreateIntrinsic(function.Realm);
        var record = iterator.SyncIteratorRecord;
        JsObject result;
        try
        {
            result = arguments.Length > 0 ? IteratorOperations.IteratorNext(record, arguments[0]) : IteratorOperations.IteratorNext(record);
        }
        catch (JsException ex)
        {
            capability.Reject(ex.Value);
            return capability.Promise;
        }

        return Continuation(function.Realm, result, capability, record, closeOnRejection: true);
    }

    private static JsValue Return(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var iterator = (AsyncFromSyncIterator)thisValue.Cell!;
        var realm = function.Realm;
        var capability = PromiseCapability.CreateIntrinsic(realm);
        var record = iterator.SyncIteratorRecord;
        try
        {
            var returnMethod = JsOperations.GetMethod(record.Iterator, new PropertyKey("return"));
            if (returnMethod is null)
            {
                capability.Resolve(IteratorOperations.CreateIteratorResultObject(realm, arguments.Length > 0 ? arguments[0] : default, done: true));
                return capability.Promise;
            }

            var result = arguments.Length > 0 ? returnMethod.Call(record.Iterator, [arguments[0]]) : returnMethod.Call(record.Iterator, []);
            if (result.Cell is not JsObject resultObject)
            {
                capability.Reject(realm.CreateError(ErrorType.TypeError, "Iterator result is not an object"));
                return capability.Promise;
            }

            return Continuation(realm, resultObject, capability, record, closeOnRejection: false);
        }
        catch (JsException ex)
        {
            capability.Reject(ex.Value);
            return capability.Promise;
        }
    }

    private static JsValue Throw(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var iterator = (AsyncFromSyncIterator)thisValue.Cell!;
        var realm = function.Realm;
        var capability = PromiseCapability.CreateIntrinsic(realm);
        var record = iterator.SyncIteratorRecord;
        try
        {
            var throwMethod = JsOperations.GetMethod(record.Iterator, new PropertyKey("throw"));
            if (throwMethod is null)
            {
                // The protocol is violated: close the iterator, then reject with a TypeError
                record.Done = true;
                IteratorOperations.IteratorClose(record);
                capability.Reject(realm.CreateError(ErrorType.TypeError, "The iterator does not provide a 'throw' method"));
                return capability.Promise;
            }

            var result = throwMethod.Call(record.Iterator, [arguments.Length > 0 ? arguments[0] : default]);
            if (result.Cell is not JsObject resultObject)
            {
                capability.Reject(realm.CreateError(ErrorType.TypeError, "Iterator result is not an object"));
                return capability.Promise;
            }

            return Continuation(realm, resultObject, capability, record, closeOnRejection: true);
        }
        catch (JsException ex)
        {
            capability.Reject(ex.Value);
            return capability.Promise;
        }
    }

    private sealed record ContinuationState(JsRealm Realm, PromiseCapability Capability, IteratorRecord Record, bool Done, bool CloseOnRejection);

    /// <summary>AsyncFromSyncIteratorContinuation(result, promiseCapability, syncIteratorRecord, closeOnRejection)</summary>
    private static JsValue Continuation(JsRealm realm, JsObject result, PromiseCapability capability, IteratorRecord record, bool closeOnRejection)
    {
        bool done;
        JsValue value;
        try
        {
            done = IteratorOperations.IteratorComplete(result);
            value = IteratorOperations.IteratorValue(result);
        }
        catch (JsException ex)
        {
            capability.Reject(ex.Value);
            return capability.Promise;
        }

        JsPromise valueWrapper;
        try
        {
            valueWrapper = JsPromise.PromiseResolve(realm, value);
        }
        catch (JsException ex)
        {
            if (!done && closeOnRejection)
            {
                IteratorOperations.IteratorCloseOnThrow(record);
            }

            capability.Reject(ex.Value);
            return capability.Promise;
        }

        var state = new ContinuationState(realm, capability, record, done, closeOnRejection);
        JsPromise.PerformPromiseThen(
            valueWrapper,
            static (s, v) =>
            {
                var c = (ContinuationState)s!;
                c.Capability.Resolve(IteratorOperations.CreateIteratorResultObject(c.Realm, v, c.Done));
            },
            static (s, error) =>
            {
                var c = (ContinuationState)s!;
                if (!c.Done && c.CloseOnRejection)
                {
                    IteratorOperations.IteratorCloseOnThrow(c.Record);
                }

                c.Capability.Reject(error);
            },
            state);
        return capability.Promise;
    }
}
