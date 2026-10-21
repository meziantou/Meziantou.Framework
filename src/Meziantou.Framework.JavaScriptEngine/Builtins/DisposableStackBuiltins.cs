namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>[[DisposableState]] / [[AsyncDisposableState]]</summary>
internal enum DisposableState : byte
{
    Pending,
    Disposed,
}

/// <summary>A DisposableStack (<see cref="ObjectClass.DisposableStack"/>) or AsyncDisposableStack (<see cref="ObjectClass.AsyncDisposableStack"/>) instance.</summary>
internal sealed class JsDisposableStack(JsObject prototype, ObjectClass objectClass) : JsObject(prototype, objectClass)
{
    /// <summary>[[DisposableState]] / [[AsyncDisposableState]]</summary>
    public DisposableState State { get; set; }

    /// <summary>[[DisposeCapability]]</summary>
    public DisposeCapability Capability { get; set; } = DisposableOperations.NewDisposeCapability();
}

/// <summary>The DisposableStack constructor and DisposableStack.prototype (https://tc39.es/proposal-explicit-resource-management/#sec-disposablestack-objects).</summary>
internal static class DisposableStackBuiltins
{
    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("DisposableStack", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "adopt", 2, Adopt);
        realm.DefineMethod(prototype, "defer", 1, Defer);
        var dispose = realm.DefineMethod(prototype, "dispose", 0, Dispose);
        realm.DefineAccessor(prototype, "disposed", GetDisposed, setter: null);
        realm.DefineMethod(prototype, "move", 0, Move);
        realm.DefineMethod(prototype, "use", 1, Use);
        prototype.DefineBuiltIn(WellKnownSymbols.Dispose, dispose);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "DisposableStack", PropertyFlags.Configurable);

        intrinsics.AddGlobal("DisposableStack", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>RequireInternalSlot(disposableStack, [[DisposableState]])</summary>
    private static JsDisposableStack ThisStack(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsDisposableStack { Class: ObjectClass.DisposableStack } stack)
            return stack;

        return ThrowHelper.ThrowTypeError<JsDisposableStack>($"Method DisposableStack.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");
    }

    private static JsDisposableStack ThisPendingStack(JsValue thisValue, string methodName)
    {
        var stack = ThisStack(thisValue, methodName);
        if (stack.State is DisposableState.Disposed)
        {
            ThrowHelper.ThrowReferenceError($"DisposableStack.prototype.{methodName}: the DisposableStack is already disposed");
        }

        return stack;
    }

    /// <summary>DisposableStack()</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor DisposableStack requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.DisposableStackPrototype);
        return new JsDisposableStack(prototype, ObjectClass.DisposableStack);
    }

    /// <summary>DisposableStack.prototype.adopt(value, onDispose)</summary>
    private static JsValue Adopt(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "adopt");
        var value = GetArgument(arguments, 0);
        var onDispose = GetArgument(arguments, 1);
        if (onDispose.Cell is not JsObject { IsCallable: true } onDisposeFunction)
            return ThrowHelper.ThrowTypeError<JsValue>($"DisposableStack.prototype.adopt: {JsOperations.DescribeForError(onDispose)} is not a function");

        DisposableOperations.AddAdoptedResource(stack.Capability, value, DisposeHint.SyncDispose, onDisposeFunction);
        return value;
    }

    /// <summary>DisposableStack.prototype.defer(onDispose)</summary>
    private static JsValue Defer(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "defer");
        DisposableOperations.AddDisposableResource(stack.Capability, DisposeHint.SyncDispose, GetArgument(arguments, 0));
        return JsValue.Undefined;
    }

    /// <summary>DisposableStack.prototype.dispose() and DisposableStack.prototype[@@dispose]()</summary>
    private static JsValue Dispose(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisStack(thisValue, "dispose");
        if (stack.State is DisposableState.Disposed)
            return JsValue.Undefined;

        stack.State = DisposableState.Disposed;
        if (DisposableOperations.DisposeResources(stack.Capability, completion: null) is { } error)
            throw new JsException(error);

        return JsValue.Undefined;
    }

    /// <summary>get DisposableStack.prototype.disposed</summary>
    private static JsValue GetDisposed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisStack(thisValue, "disposed").State is DisposableState.Disposed;
    }

    /// <summary>DisposableStack.prototype.move()</summary>
    private static JsValue Move(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "move");

        // OrdinaryCreateFromConstructor(%DisposableStack%, "%DisposableStack.prototype%"): %DisposableStack%.prototype is not writable
        var newStack = new JsDisposableStack(function.Realm.Intrinsics.DisposableStackPrototype, ObjectClass.DisposableStack)
        {
            Capability = stack.Capability,
        };

        stack.Capability = DisposableOperations.NewDisposeCapability();
        stack.State = DisposableState.Disposed;
        return newStack;
    }

    /// <summary>DisposableStack.prototype.use(value)</summary>
    private static JsValue Use(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "use");
        var value = GetArgument(arguments, 0);
        DisposableOperations.AddDisposableResource(stack.Capability, value, DisposeHint.SyncDispose);
        return value;
    }
}

/// <summary>The AsyncDisposableStack constructor and AsyncDisposableStack.prototype (https://tc39.es/proposal-explicit-resource-management/#sec-asyncdisposablestack-objects).</summary>
internal static class AsyncDisposableStackBuiltins
{
    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("AsyncDisposableStack", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "adopt", 2, Adopt);
        realm.DefineMethod(prototype, "defer", 1, Defer);
        var disposeAsync = realm.DefineMethod(prototype, "disposeAsync", 0, DisposeAsync);
        realm.DefineAccessor(prototype, "disposed", GetDisposed, setter: null);
        realm.DefineMethod(prototype, "move", 0, Move);
        realm.DefineMethod(prototype, "use", 1, Use);
        prototype.DefineBuiltIn(WellKnownSymbols.AsyncDispose, disposeAsync);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "AsyncDisposableStack", PropertyFlags.Configurable);

        intrinsics.AddGlobal("AsyncDisposableStack", constructor);
        return constructor;
    }

    private static JsValue GetArgument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static string GetIncompatibleReceiverMessage(JsValue thisValue, string methodName) => $"Method AsyncDisposableStack.prototype.{methodName} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}";

    /// <summary>RequireInternalSlot(asyncDisposableStack, [[AsyncDisposableState]])</summary>
    private static JsDisposableStack ThisStack(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsDisposableStack { Class: ObjectClass.AsyncDisposableStack } stack)
            return stack;

        return ThrowHelper.ThrowTypeError<JsDisposableStack>(GetIncompatibleReceiverMessage(thisValue, methodName));
    }

    private static JsDisposableStack ThisPendingStack(JsValue thisValue, string methodName)
    {
        var stack = ThisStack(thisValue, methodName);
        if (stack.State is DisposableState.Disposed)
        {
            ThrowHelper.ThrowReferenceError($"AsyncDisposableStack.prototype.{methodName}: the AsyncDisposableStack is already disposed");
        }

        return stack;
    }

    /// <summary>AsyncDisposableStack()</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor AsyncDisposableStack requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.AsyncDisposableStackPrototype);
        return new JsDisposableStack(prototype, ObjectClass.AsyncDisposableStack);
    }

    /// <summary>AsyncDisposableStack.prototype.adopt(value, onDisposeAsync)</summary>
    private static JsValue Adopt(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "adopt");
        var value = GetArgument(arguments, 0);
        var onDisposeAsync = GetArgument(arguments, 1);
        if (onDisposeAsync.Cell is not JsObject { IsCallable: true } onDisposeAsyncFunction)
            return ThrowHelper.ThrowTypeError<JsValue>($"AsyncDisposableStack.prototype.adopt: {JsOperations.DescribeForError(onDisposeAsync)} is not a function");

        DisposableOperations.AddAdoptedResource(stack.Capability, value, DisposeHint.AsyncDispose, onDisposeAsyncFunction);
        return value;
    }

    /// <summary>AsyncDisposableStack.prototype.defer(onDisposeAsync)</summary>
    private static JsValue Defer(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "defer");
        DisposableOperations.AddDisposableResource(stack.Capability, DisposeHint.AsyncDispose, GetArgument(arguments, 0));
        return JsValue.Undefined;
    }

    /// <summary>AsyncDisposableStack.prototype.disposeAsync() and AsyncDisposableStack.prototype[@@asyncDispose]()</summary>
    private static JsValue DisposeAsync(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var realm = function.Realm;
        var promiseCapability = PromiseCapability.CreateIntrinsic(realm);
        if (thisValue.Cell is not JsDisposableStack { Class: ObjectClass.AsyncDisposableStack } stack)
        {
            promiseCapability.Reject(ThrowHelper.CreateException(ErrorType.TypeError, GetIncompatibleReceiverMessage(thisValue, "disposeAsync")).Value);
            return promiseCapability.Promise;
        }

        if (stack.State is DisposableState.Disposed)
        {
            promiseCapability.Resolve(JsValue.Undefined);
            return promiseCapability.Promise;
        }

        stack.State = DisposableState.Disposed;
        DisposableOperations.DisposeResourcesAsync(realm, stack.Capability, completion: null, promiseCapability);
        return promiseCapability.Promise;
    }

    /// <summary>get AsyncDisposableStack.prototype.disposed</summary>
    private static JsValue GetDisposed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisStack(thisValue, "disposed").State is DisposableState.Disposed;
    }

    /// <summary>AsyncDisposableStack.prototype.move()</summary>
    private static JsValue Move(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "move");
        var newStack = new JsDisposableStack(function.Realm.Intrinsics.AsyncDisposableStackPrototype, ObjectClass.AsyncDisposableStack)
        {
            Capability = stack.Capability,
        };

        stack.Capability = DisposableOperations.NewDisposeCapability();
        stack.State = DisposableState.Disposed;
        return newStack;
    }

    /// <summary>AsyncDisposableStack.prototype.use(value)</summary>
    private static JsValue Use(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var stack = ThisPendingStack(thisValue, "use");
        var value = GetArgument(arguments, 0);
        DisposableOperations.AddDisposableResource(stack.Capability, value, DisposeHint.AsyncDispose);
        return value;
    }
}

/// <summary>The SuppressedError constructor and SuppressedError.prototype (https://tc39.es/proposal-explicit-resource-management/#sec-suppressederror-objects).</summary>
internal static class SuppressedErrorBuiltins
{
    public static JsFunction Initialize(Intrinsics intrinsics, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("SuppressedError", 3, Construct, isConstructor: true, prototype: intrinsics.GetErrorConstructor(ErrorType.Error));
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn("message", "");
        prototype.DefineBuiltIn("name", "SuppressedError");

        intrinsics.AddGlobal("SuppressedError", constructor);
        return constructor;
    }

    /// <summary>SuppressedError(error, suppressed, message)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.SuppressedErrorPrototype);
        var error = new JsErrorObject(prototype);
        var message = arguments.Length > 2 ? arguments[2] : default;
        if (!message.IsUndefined)
        {
            ObjectOperations.CreateNonEnumerableDataPropertyOrThrow(error, "message", JsValue.FromJsString(JsOperations.ToJsString(message)));
        }

        ObjectOperations.CreateNonEnumerableDataPropertyOrThrow(error, "error", arguments.Length > 0 ? arguments[0] : default);
        ObjectOperations.CreateNonEnumerableDataPropertyOrThrow(error, "suppressed", arguments.Length > 1 ? arguments[1] : default);
        error.CaptureStackTrace(function.Realm.Engine);
        return error;
    }
}

/// <summary>%AsyncIteratorPrototype%[@@asyncDispose]</summary>
internal static class AsyncIteratorDisposeBuiltins
{
    private static readonly PropertyKey ReturnKey = new("return");
    private static readonly PromiseContinuation ResolveUndefined = static (state, _) => ((PromiseCapability)state!).Resolve(JsValue.Undefined);
    private static readonly PromiseContinuation Reject = static (state, reason) => ((PromiseCapability)state!).Reject(reason);

    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        realm.DefineMethod(intrinsics.AsyncIteratorPrototype, WellKnownSymbols.AsyncDispose, 0, AsyncIteratorAsyncDispose);
    }

    /// <summary>%AsyncIteratorPrototype%[@@asyncDispose]()</summary>
    private static JsValue AsyncIteratorAsyncDispose(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var realm = function.Realm;
        var promiseCapability = PromiseCapability.CreateIntrinsic(realm);
        JsPromise resultWrapper;
        try
        {
            var returnMethod = JsOperations.GetMethod(thisValue, ReturnKey);
            if (returnMethod is null)
            {
                promiseCapability.Resolve(JsValue.Undefined);
                return promiseCapability.Promise;
            }

            var result = returnMethod.Call(thisValue, []);
            resultWrapper = JsPromise.PromiseResolve(realm, result);
        }
        catch (JsException exception)
        {
            promiseCapability.Reject(exception.Value);
            return promiseCapability.Promise;
        }

        // PerformPromiseThen(resultWrapper, unwrap, undefined, promiseCapability): the handlers are not observable
        JsPromise.PerformPromiseThen(resultWrapper, ResolveUndefined, Reject, promiseCapability);
        return promiseCapability.Promise;
    }
}
