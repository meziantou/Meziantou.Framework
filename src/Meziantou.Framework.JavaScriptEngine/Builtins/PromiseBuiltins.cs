namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Promise constructor and Promise.prototype (https://tc39.es/ecma262/#sec-promise-objects).</summary>
internal static class PromiseBuiltins
{
    private static readonly PropertyKey ThenKey = new("then");
    private static readonly PropertyKey StatusKey = new("status");
    private static readonly PropertyKey ValueKey = new("value");
    private static readonly PropertyKey ReasonKey = new("reason");

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype, out JsFunction then)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("Promise", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "all", 1, All);
        realm.DefineMethod(constructor, "allSettled", 1, AllSettled);
        realm.DefineMethod(constructor, "any", 1, Any);
        realm.DefineMethod(constructor, "race", 1, Race);
        realm.DefineMethod(constructor, "reject", 1, Reject);
        realm.DefineMethod(constructor, "resolve", 1, Resolve);
        realm.DefineMethod(constructor, "try", 1, Try);
        realm.DefineMethod(constructor, "withResolvers", 0, WithResolvers);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, static (_, thisValue, _, _) => thisValue, setter: null);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "catch", 1, Catch);
        realm.DefineMethod(prototype, "finally", 1, Finally);
        then = realm.DefineMethod(prototype, "then", 2, Then);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Promise", PropertyFlags.Configurable);

        intrinsics.AddGlobal("Promise", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>Promise(executor)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Promise requires 'new'");

        var executor = GetArgument(arguments, 0);
        if (!executor.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>($"Promise resolver {JsOperations.DescribeForError(executor)} is not a function");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.PromisePrototype);
        var promise = new JsPromise(prototype);
        var resolvingFunctions = new PromiseResolvingFunctions(promise, function.Realm);
        var resolve = resolvingFunctions.ResolveFunction;
        var reject = resolvingFunctions.RejectFunction;
        try
        {
            executor.AsObject().Call(JsValue.Undefined, [resolve, reject]);
        }
        catch (JsException exception)
        {
            reject.Call(JsValue.Undefined, [exception.Value]);
        }

        return promise;
    }

    /// <summary>IfAbruptRejectPromise(value, capability): rejects the promise of the capability with the thrown value and returns the promise.</summary>
    private static JsValue RejectCapability(PromiseCapability capability, JsException exception)
    {
        capability.Reject(exception.Value);
        return capability.Promise;
    }

    private delegate JsValue PerformCombinator(JsBuiltinFunction function, IteratorRecord iteratorRecord, JsObject constructor, PromiseCapability capability, JsValue promiseResolve);

    /// <summary>The steps shared by Promise.all, allSettled, any and race.</summary>
    private static JsValue RunCombinator(JsBuiltinFunction function, JsValue thisValue, JsValue iterable, PerformCombinator perform)
    {
        var capability = PromiseCapability.Create(thisValue);
        var constructor = thisValue.AsObject();
        JsValue promiseResolve;
        IteratorRecord iteratorRecord;
        try
        {
            promiseResolve = PromiseCapability.GetPromiseResolve(constructor);
            iteratorRecord = IteratorOperations.GetIterator(iterable);
        }
        catch (JsException exception)
        {
            return RejectCapability(capability, exception);
        }

        try
        {
            return perform(function, iteratorRecord, constructor, capability, promiseResolve);
        }
        catch (JsException exception)
        {
            if (!iteratorRecord.Done)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
            }

            return RejectCapability(capability, exception);
        }
    }

    /// <summary>Promise.all(iterable)</summary>
    private static JsValue All(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return RunCombinator(function, thisValue, GetArgument(arguments, 0), PerformPromiseAll);
    }

    /// <summary>PerformPromiseAll(iteratorRecord, constructor, resultCapability, promiseResolve)</summary>
    private static JsValue PerformPromiseAll(JsBuiltinFunction function, IteratorRecord iteratorRecord, JsObject constructor, PromiseCapability capability, JsValue promiseResolve)
    {
        var state = new CombinatorState(capability);
        var index = 0;
        while (true)
        {
            if (!IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
            {
                state.RemainingElements--;
                if (state.RemainingElements == 0)
                {
                    capability.Resolve(JsArray.Create(function.Realm, state.ValuesAsSpan()));
                }

                return capability.Promise;
            }

            state.Values.Add(JsValue.Undefined);
            var nextPromise = JsOperations.Call(promiseResolve, constructor, [next]);
            var onFulfilled = CreateElementFunction(state, index, alreadyCalled: null, AllResolveElement);
            index++;
            state.RemainingElements++;
            JsOperations.Invoke(nextPromise, ThenKey, [onFulfilled, capability.RejectFunction]);
        }
    }

    /// <summary>Promise.all resolve element functions</summary>
    private static JsValue AllResolveElement(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var element = (ElementFunctionState)function.State!;
        if (!element.TryMarkCalled())
            return JsValue.Undefined;

        var state = element.Combinator;
        state.Values[element.Index] = GetArgument(arguments, 0);
        return state.ElementSettled(fulfill: true);
    }

    /// <summary>Promise.allSettled(iterable)</summary>
    private static JsValue AllSettled(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return RunCombinator(function, thisValue, GetArgument(arguments, 0), PerformPromiseAllSettled);
    }

    /// <summary>PerformPromiseAllSettled(iteratorRecord, constructor, resultCapability, promiseResolve)</summary>
    private static JsValue PerformPromiseAllSettled(JsBuiltinFunction function, IteratorRecord iteratorRecord, JsObject constructor, PromiseCapability capability, JsValue promiseResolve)
    {
        var state = new CombinatorState(capability);
        var index = 0;
        while (true)
        {
            if (!IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
            {
                state.RemainingElements--;
                if (state.RemainingElements == 0)
                {
                    capability.Resolve(JsArray.Create(function.Realm, state.ValuesAsSpan()));
                }

                return capability.Promise;
            }

            state.Values.Add(JsValue.Undefined);
            var nextPromise = JsOperations.Call(promiseResolve, constructor, [next]);
            var alreadyCalled = new AlreadyCalledRecord();
            var onFulfilled = CreateElementFunction(state, index, alreadyCalled, AllSettledResolveElement);
            var onRejected = CreateElementFunction(state, index, alreadyCalled, AllSettledRejectElement);
            index++;
            state.RemainingElements++;
            JsOperations.Invoke(nextPromise, ThenKey, [onFulfilled, onRejected]);
        }
    }

    /// <summary>Promise.allSettled resolve element functions</summary>
    private static JsValue AllSettledResolveElement(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return AllSettledElement(function, GetArgument(arguments, 0), fulfilled: true);
    }

    /// <summary>Promise.allSettled reject element functions</summary>
    private static JsValue AllSettledRejectElement(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return AllSettledElement(function, GetArgument(arguments, 0), fulfilled: false);
    }

    private static JsValue AllSettledElement(JsBuiltinFunction function, JsValue value, bool fulfilled)
    {
        var element = (ElementFunctionState)function.State!;
        if (!element.TryMarkCalled())
            return JsValue.Undefined;

        // The object is created in the current realm: the realm of the element function
        var obj = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        obj.CreateDataPropertyOrThrow(StatusKey, fulfilled ? "fulfilled" : "rejected");
        obj.CreateDataPropertyOrThrow(fulfilled ? ValueKey : ReasonKey, value);
        var state = element.Combinator;
        state.Values[element.Index] = obj;
        return state.ElementSettled(fulfill: true);
    }

    /// <summary>Promise.any(iterable)</summary>
    private static JsValue Any(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return RunCombinator(function, thisValue, GetArgument(arguments, 0), PerformPromiseAny);
    }

    /// <summary>PerformPromiseAny(iteratorRecord, constructor, resultCapability, promiseResolve)</summary>
    private static JsValue PerformPromiseAny(JsBuiltinFunction function, IteratorRecord iteratorRecord, JsObject constructor, PromiseCapability capability, JsValue promiseResolve)
    {
        var state = new CombinatorState(capability);
        var index = 0;
        while (true)
        {
            if (!IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
            {
                state.RemainingElements--;
                if (state.RemainingElements == 0)
                {
                    capability.Reject(CreateAggregateError(function.Realm, state));
                }

                return capability.Promise;
            }

            state.Values.Add(JsValue.Undefined);
            var nextPromise = JsOperations.Call(promiseResolve, constructor, [next]);
            var onRejected = CreateElementFunction(state, index, alreadyCalled: null, AnyRejectElement);
            index++;
            state.RemainingElements++;
            JsOperations.Invoke(nextPromise, ThenKey, [capability.ResolveFunction, onRejected]);
        }
    }

    /// <summary>Promise.any reject element functions</summary>
    private static JsValue AnyRejectElement(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var element = (ElementFunctionState)function.State!;
        if (!element.TryMarkCalled())
            return JsValue.Undefined;

        var state = element.Combinator;
        state.Values[element.Index] = GetArgument(arguments, 0);
        return state.ElementSettled(fulfill: false);
    }

    /// <summary>A newly created AggregateError object whose "errors" property is the list of the errors.</summary>
    private static JsErrorObject CreateAggregateError(JsRealm realm, CombinatorState state)
    {
        var error = new JsErrorObject(realm.Intrinsics.GetErrorPrototype(ErrorType.AggregateError));
        error.DefinePropertyOrThrow("errors", PropertyDescriptor.Data(JsArray.Create(realm, state.ValuesAsSpan()), PropertyFlags.Writable | PropertyFlags.Configurable));
        return error;
    }

    /// <summary>Promise.race(iterable)</summary>
    private static JsValue Race(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return RunCombinator(function, thisValue, GetArgument(arguments, 0), PerformPromiseRace);
    }

    /// <summary>PerformPromiseRace(iteratorRecord, constructor, resultCapability, promiseResolve)</summary>
    private static JsValue PerformPromiseRace(JsBuiltinFunction function, IteratorRecord iteratorRecord, JsObject constructor, PromiseCapability capability, JsValue promiseResolve)
    {
        while (IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
        {
            var nextPromise = JsOperations.Call(promiseResolve, constructor, [next]);
            JsOperations.Invoke(nextPromise, ThenKey, [capability.ResolveFunction, capability.RejectFunction]);
        }

        return capability.Promise;
    }

    /// <summary>Promise.reject(r)</summary>
    private static JsValue Reject(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var capability = PromiseCapability.Create(thisValue);
        capability.Reject(GetArgument(arguments, 0));
        return capability.Promise;
    }

    /// <summary>Promise.resolve(x)</summary>
    private static JsValue Resolve(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject constructor)
            return ThrowHelper.ThrowTypeError<JsValue>("Promise.resolve called on non-object");

        return JsPromise.PromiseResolve(constructor, GetArgument(arguments, 0));
    }

    /// <summary>Promise.try(callback, ...args)</summary>
    private static JsValue Try(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject constructor)
            return ThrowHelper.ThrowTypeError<JsValue>("Promise.try called on non-object");

        JsValue result;
        try
        {
            result = JsOperations.Call(GetArgument(arguments, 0), JsValue.Undefined, arguments.Length > 1 ? arguments[1..] : []);
        }
        catch (JsException exception)
        {
            var capability = PromiseCapability.Create(constructor);
            capability.Reject(exception.Value);
            return capability.Promise;
        }

        return JsPromise.PromiseResolve(constructor, result);
    }

    /// <summary>Promise.withResolvers()</summary>
    private static JsValue WithResolvers(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var capability = PromiseCapability.Create(thisValue);
        var obj = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        obj.CreateDataPropertyOrThrow("promise", capability.Promise);
        obj.CreateDataPropertyOrThrow("resolve", capability.ResolveFunction);
        obj.CreateDataPropertyOrThrow("reject", capability.RejectFunction);
        return obj;
    }

    /// <summary>Promise.prototype.catch(onRejected)</summary>
    private static JsValue Catch(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return JsOperations.Invoke(thisValue, ThenKey, [JsValue.Undefined, GetArgument(arguments, 0)]);
    }

    /// <summary>Promise.prototype.finally(onFinally)</summary>
    private static JsValue Finally(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject promise)
            return ThrowHelper.ThrowTypeError<JsValue>("Promise.prototype.finally called on non-object");

        var constructor = ObjectOperations.SpeciesConstructor(promise, function.Realm.Intrinsics.Promise);
        var onFinally = GetArgument(arguments, 0);
        JsValue thenFinally;
        JsValue catchFinally;
        if (!onFinally.IsCallable)
        {
            thenFinally = onFinally;
            catchFinally = onFinally;
        }
        else
        {
            var state = new FinallyState(onFinally, constructor);
            var realm = JsEngine.Current!.CurrentRealm!;
            var thenFinallyFunction = realm.CreateBuiltinFunction("", 1, ThenFinally);
            thenFinallyFunction.State = state;
            var catchFinallyFunction = realm.CreateBuiltinFunction("", 1, CatchFinally);
            catchFinallyFunction.State = state;
            thenFinally = thenFinallyFunction;
            catchFinally = catchFinallyFunction;
        }

        return JsOperations.Invoke(promise, ThenKey, [thenFinally, catchFinally]);
    }

    /// <summary>The thenFinally closure of Promise.prototype.finally.</summary>
    private static JsValue ThenFinally(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var state = (FinallyState)function.State!;
        var value = GetArgument(arguments, 0);
        var result = JsOperations.Call(state.OnFinally, JsValue.Undefined, []);
        var promise = JsPromise.PromiseResolve(state.Constructor, result);
        var valueThunk = function.Realm.CreateBuiltinFunction("", 0, static (f, _, _, _) => (JsValue)f.State2!);
        valueThunk.State2 = value;
        return JsOperations.Invoke(promise, ThenKey, [valueThunk]);
    }

    /// <summary>The catchFinally closure of Promise.prototype.finally.</summary>
    private static JsValue CatchFinally(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var state = (FinallyState)function.State!;
        var reason = GetArgument(arguments, 0);
        var result = JsOperations.Call(state.OnFinally, JsValue.Undefined, []);
        var promise = JsPromise.PromiseResolve(state.Constructor, result);
        var thrower = function.Realm.CreateBuiltinFunction("", 0, static (f, _, _, _) => throw new JsException((JsValue)f.State2!));
        thrower.State2 = reason;
        return JsOperations.Invoke(promise, ThenKey, [thrower]);
    }

    /// <summary>Promise.prototype.then(onFulfilled, onRejected)</summary>
    private static JsValue Then(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsPromise promise)
            return ThrowHelper.ThrowTypeError<JsValue>("Promise.prototype.then called on incompatible receiver");

        var constructor = ObjectOperations.SpeciesConstructor(promise, function.Realm.Intrinsics.Promise);
        var capability = PromiseCapability.Create(constructor);
        return JsPromise.PerformPromiseThen(promise, GetArgument(arguments, 0), GetArgument(arguments, 1), capability);
    }

    private static JsBuiltinFunction CreateElementFunction(CombinatorState state, int index, AlreadyCalledRecord? alreadyCalled, BuiltinFunctionCallback callback)
    {
        // The element functions are created in the current realm
        var function = JsEngine.Current!.CurrentRealm!.CreateBuiltinFunction("", 1, callback);
        function.State = new ElementFunctionState(state, index, alreadyCalled);
        return function;
    }

    /// <summary>The values, the capability and the remainingElementsCount record shared by the element functions of a combinator.</summary>
    private sealed class CombinatorState(PromiseCapability capability)
    {
        public PromiseCapability Capability { get; } = capability;

        public List<JsValue> Values { get; } = [];

        public int RemainingElements { get; set; } = 1;

        public ReadOnlySpan<JsValue> ValuesAsSpan() => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Values);

        /// <summary>Decrements remainingElementsCount; when it reaches 0, resolves (all, allSettled) or rejects (any) the capability.</summary>
        public JsValue ElementSettled(bool fulfill)
        {
            RemainingElements--;
            if (RemainingElements != 0)
                return JsValue.Undefined;

            // The array (and the AggregateError) are created in the current realm: the realm of the element function
            var realm = JsEngine.Current!.CurrentRealm!;
            if (fulfill)
            {
                Capability.Resolve(JsArray.Create(realm, ValuesAsSpan()));
            }
            else
            {
                Capability.Reject(CreateAggregateError(realm, this));
            }

            return JsValue.Undefined;
        }
    }

    /// <summary>[[AlreadyCalled]] shared by the two element functions of Promise.allSettled.</summary>
    private sealed class AlreadyCalledRecord
    {
        public bool Value { get; set; }
    }

    /// <summary>The internal slots of an element function: [[AlreadyCalled]], [[Index]], and the captured state.</summary>
    private sealed class ElementFunctionState(CombinatorState combinator, int index, AlreadyCalledRecord? sharedAlreadyCalled)
    {
        private bool _alreadyCalled;

        public CombinatorState Combinator { get; } = combinator;

        public int Index { get; } = index;

        public bool TryMarkCalled()
        {
            if (sharedAlreadyCalled is not null)
            {
                if (sharedAlreadyCalled.Value)
                    return false;

                sharedAlreadyCalled.Value = true;
                return true;
            }

            if (_alreadyCalled)
                return false;

            _alreadyCalled = true;
            return true;
        }
    }

    private sealed class FinallyState(JsValue onFinally, JsObject constructor)
    {
        public JsValue OnFinally { get; } = onFinally;

        public JsObject Constructor { get; } = constructor;
    }
}
