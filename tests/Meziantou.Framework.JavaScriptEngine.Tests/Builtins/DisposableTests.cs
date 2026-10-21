namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class DisposableTests
{
    private static JsValue Get(JsValue target, string name) => JsOperations.GetV(target, new PropertyKey(name));

    private static JsValue Invoke(JsValue target, string name, params JsValue[] arguments) => JsOperations.Invoke(target, new PropertyKey(name), arguments);

    private static JsPromise Settle(TestRealm test, JsValue promise)
    {
        test.Engine.RunJobs();
        return (JsPromise)promise.AsObject();
    }

    private static JsValue Logger(TestRealm test, List<string> log, string message, JsValue result = default)
    {
        return test.CreateFunction((_, _) =>
        {
            log.Add(message);
            return result;
        });
    }

    private static JsValue Thrower(TestRealm test, List<string> log, string message, JsValue error)
    {
        return test.CreateFunction((_, _) =>
        {
            log.Add(message);
            throw new JsException(error);
        });
    }

    private static JsObject Disposable(TestRealm test, JsSymbol symbol, JsValue method)
    {
        var obj = test.CreateObject();
        obj.CreateDataPropertyOrThrow(new PropertyKey(symbol), method);
        return obj;
    }

    [Fact]
    public void SuppressedError_Properties()
    {
        using var test = new TestRealm();
        var constructor = test.Get("SuppressedError").AsObject();
        Assert.Equal(3, Get(constructor, "length").AsNumber());
        Assert.Equal("SuppressedError", Get(constructor, "name").AsString());
        Assert.Same(test.Get("Error").AsObject(), constructor.GetPrototypeOf());

        var prototype = Get(constructor, "prototype").AsObject();
        Assert.Same(test.Realm.Intrinsics.GetErrorPrototype(ErrorType.Error), prototype.GetPrototypeOf());
        Assert.Equal("SuppressedError", Get(prototype, "name").AsString());
        Assert.Equal("", Get(prototype, "message").AsString());
        Assert.False(prototype.HasOwnProperty(new PropertyKey("error")));
        Assert.False(prototype.HasOwnProperty(new PropertyKey("suppressed")));
    }

    [Fact]
    public void SuppressedError_Construct()
    {
        using var test = new TestRealm();
        var error = test.CreateObject();
        var suppressed = test.CreateObject();
        var instance = test.Construct("SuppressedError", error, suppressed, 42).AsObject();
        Assert.Equal(ObjectClass.Error, instance.Class);
        Assert.Equal(["message", "error", "suppressed"], instance.OwnPropertyKeys().Select(key => key.AsString()).ToArray());
        Assert.Equal("42", Get(instance, "message").AsString());
        Assert.Same(error, Get(instance, "error").AsObject());
        Assert.Same(suppressed, Get(instance, "suppressed").AsObject());
        Assert.True(instance.GetOwnProperty(new PropertyKey("error"), out var descriptor));
        Assert.True(descriptor.Writable && descriptor.Configurable && !descriptor.Enumerable);

        // Called as a function, and without a message
        var called = test.Call("SuppressedError").AsObject();
        Assert.False(called.HasOwnProperty(new PropertyKey("message")));
        Assert.True(called.HasOwnProperty(new PropertyKey("error")));
        Assert.Same(test.Realm.Intrinsics.SuppressedErrorPrototype, called.GetPrototypeOf());
    }

    [Fact]
    public void DisposableStack_Properties()
    {
        using var test = new TestRealm();
        var constructor = test.Get("DisposableStack").AsObject();
        Assert.Equal(0, Get(constructor, "length").AsNumber());
        Assert.Equal("TypeError", test.GetErrorName(() => test.Call("DisposableStack")));

        var prototype = test.Get("DisposableStack.prototype");
        Assert.Equal(2, Get(Get(prototype, "adopt"), "length").AsNumber());
        Assert.Equal(1, Get(Get(prototype, "defer"), "length").AsNumber());
        Assert.Equal(0, Get(Get(prototype, "dispose"), "length").AsNumber());
        Assert.Equal(1, Get(Get(prototype, "use"), "length").AsNumber());
        Assert.Equal(0, Get(Get(prototype, "move"), "length").AsNumber());
        Assert.Same(Get(prototype, "dispose").AsObject(), JsOperations.GetV(prototype, new PropertyKey(WellKnownSymbols.Dispose)).AsObject());
        Assert.Equal("DisposableStack", JsOperations.GetV(prototype, new PropertyKey(WellKnownSymbols.ToStringTag)).AsString());
        Assert.True(prototype.AsObject().GetOwnProperty(new PropertyKey("disposed"), out var disposed));
        Assert.Equal("get disposed", Get(disposed.Get, "name").AsString());
        Assert.True(disposed.Set.IsUndefined);
    }

    [Fact]
    public void DisposableStack_DisposesInReverseOrder()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var stack = test.Construct("DisposableStack");
        var resource = Disposable(test, WellKnownSymbols.Dispose, Logger(test, log, "use"));
        Assert.Same(resource, Invoke(stack, "use", resource).AsObject());
        Assert.True(Invoke(stack, "use", JsValue.Null).IsNull);
        Assert.True(Invoke(stack, "use", JsValue.Undefined).IsUndefined);
        var adopted = test.CreateObject();
        Invoke(stack, "adopt", adopted, test.CreateFunction((thisValue, arguments) =>
        {
            Assert.True(thisValue.IsUndefined);
            Assert.Same(adopted, arguments[0].AsObject());
            log.Add("adopt");
            return default;
        }));
        Assert.True(Invoke(stack, "defer", Logger(test, log, "defer")).IsUndefined);

        Assert.False(Get(stack, "disposed").AsBoolean());
        Assert.True(Invoke(stack, "dispose").IsUndefined);
        Assert.True(Get(stack, "disposed").AsBoolean());
        Assert.Equal(["defer", "adopt", "use"], log);

        Assert.True(Invoke(stack, "dispose").IsUndefined);
        Assert.HasCount(3, log);
        Assert.Equal("ReferenceError", test.GetErrorName(() => Invoke(stack, "use", resource)));
        Assert.Equal("ReferenceError", test.GetErrorName(() => Invoke(stack, "defer", Logger(test, log, "x"))));
        Assert.Equal("ReferenceError", test.GetErrorName(() => Invoke(stack, "move")));
    }

    [Fact]
    public void DisposableStack_Errors()
    {
        using var test = new TestRealm();
        var stack = test.Construct("DisposableStack");
        Assert.Equal("TypeError", test.GetErrorName(() => Invoke(stack, "use", 1)));
        Assert.Equal("TypeError", test.GetErrorName(() => Invoke(stack, "use", test.CreateObject())));
        Assert.Equal("TypeError", test.GetErrorName(() => Invoke(stack, "use", Disposable(test, WellKnownSymbols.Dispose, 1))));
        Assert.Equal("TypeError", test.GetErrorName(() => Invoke(stack, "use", Disposable(test, WellKnownSymbols.AsyncDispose, test.CreateFunction((_, _) => default)))));
        Assert.Equal("TypeError", test.GetErrorName(() => Invoke(stack, "defer", test.CreateObject())));
        Assert.Equal("TypeError", test.GetErrorName(() => Invoke(stack, "adopt", 1, 2)));
        Assert.Equal("TypeError", test.GetErrorName(() => test.CallWithThis(test.CreateObject(), "DisposableStack.prototype.dispose")));
        Assert.Equal("TypeError", test.GetErrorName(() => test.CallWithThis(test.Construct("AsyncDisposableStack"), "DisposableStack.prototype.use", JsValue.Null)));
        Assert.Equal("TypeError", test.GetErrorName(() => test.CallWithThis(1, "DisposableStack.prototype.move")));
    }

    [Fact]
    public void DisposableStack_SingleErrorIsThrownAsIs()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var error = test.CreateObject();
        var stack = test.Construct("DisposableStack");
        Invoke(stack, "defer", Logger(test, log, "first"));
        Invoke(stack, "defer", Thrower(test, log, "throws", error));
        Invoke(stack, "defer", Logger(test, log, "last"));
        Assert.Same(error, test.GetError(() => Invoke(stack, "dispose")));
        Assert.Equal(["last", "throws", "first"], log);
    }

    [Fact]
    public void DisposableStack_SuppressedErrorChaining()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var error1 = test.CreateObject();
        var error2 = test.CreateObject();
        var error3 = test.CreateObject();
        var stack = test.Construct("DisposableStack");
        Invoke(stack, "defer", Thrower(test, log, "1", error1));
        Invoke(stack, "defer", Thrower(test, log, "2", error2));
        Invoke(stack, "defer", Thrower(test, log, "3", error3));
        var error = test.GetError(() => Invoke(stack, "dispose"));
        Assert.Equal(["3", "2", "1"], log);
        Assert.Same(test.Realm.Intrinsics.SuppressedErrorPrototype, error.GetPrototypeOf());
        Assert.False(error.HasOwnProperty(new PropertyKey("message")));
        Assert.Same(error1, Get(error, "error").AsObject());
        var inner = Get(error, "suppressed").AsObject();
        Assert.Same(test.Realm.Intrinsics.SuppressedErrorPrototype, inner.GetPrototypeOf());
        Assert.Same(error2, Get(inner, "error").AsObject());
        Assert.Same(error3, Get(inner, "suppressed").AsObject());
    }

    [Fact]
    public void DisposeResources_ThrowCompletionIsSuppressed()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var bodyError = test.CreateObject();
        var disposeError = test.CreateObject();
        var capability = DisposableOperations.NewDisposeCapability();
        DisposableOperations.AddDisposableResource(capability, Disposable(test, WellKnownSymbols.Dispose, Logger(test, log, "a")), DisposeHint.SyncDispose);
        DisposableOperations.AddDisposableResource(capability, JsValue.Null, DisposeHint.SyncDispose);
        DisposableOperations.AddDisposableResource(capability, Disposable(test, WellKnownSymbols.Dispose, Thrower(test, log, "b", disposeError)), DisposeHint.SyncDispose);
        Assert.Equal(2, capability.Count);

        var result = DisposableOperations.DisposeResources(capability, bodyError);
        Assert.Equal(["b", "a"], log);
        Assert.Equal(0, capability.Count);
        var error = result!.Value.AsObject();
        Assert.Same(disposeError, Get(error, "error").AsObject());
        Assert.Same(bodyError, Get(error, "suppressed").AsObject());

        // A normal completion stays normal when the disposals succeed
        DisposableOperations.AddDisposableResource(capability, Disposable(test, WellKnownSymbols.Dispose, Logger(test, log, "c")), DisposeHint.SyncDispose);
        Assert.Null(DisposableOperations.DisposeResources(capability, completion: null));
    }

    [Fact]
    public void DisposableStack_Move()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var stack = test.Construct("DisposableStack");
        Invoke(stack, "defer", Logger(test, log, "a"));
        var moved = Invoke(stack, "move");
        Assert.Empty(log);
        Assert.True(Get(stack, "disposed").AsBoolean());
        Assert.False(Get(moved, "disposed").AsBoolean());
        Assert.Same(test.Realm.Intrinsics.DisposableStackPrototype, moved.AsObject().GetPrototypeOf());
        Invoke(moved, "dispose");
        Assert.Equal(["a"], log);
    }

    [Fact]
    public void AsyncDisposableStack_Properties()
    {
        using var test = new TestRealm();
        var prototype = test.Get("AsyncDisposableStack.prototype");
        Assert.Same(Get(prototype, "disposeAsync").AsObject(), JsOperations.GetV(prototype, new PropertyKey(WellKnownSymbols.AsyncDispose)).AsObject());
        Assert.Equal("AsyncDisposableStack", JsOperations.GetV(prototype, new PropertyKey(WellKnownSymbols.ToStringTag)).AsString());
        Assert.Equal(0, Get(Get(prototype, "disposeAsync"), "length").AsNumber());
        Assert.Equal("TypeError", test.GetErrorName(() => test.Call("AsyncDisposableStack")));
    }

    [Fact]
    public void AsyncDisposableStack_DisposesInReverseOrderAndAwaits()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var stack = test.Construct("AsyncDisposableStack");
        Invoke(stack, "use", Disposable(test, WellKnownSymbols.Dispose, Logger(test, log, "sync")));
        Invoke(stack, "use", Disposable(test, WellKnownSymbols.AsyncDispose, test.CreateFunction((_, _) =>
        {
            log.Add("async");
            return Invoke(test.Call("Promise.resolve"), "then", Logger(test, log, "async resolved"));
        })));
        Invoke(stack, "adopt", 1, Logger(test, log, "adopt"));
        Invoke(stack, "defer", Logger(test, log, "defer"));

        var promise = Invoke(stack, "disposeAsync");
        Assert.Equal(["defer"], log);
        Assert.True(Get(stack, "disposed").AsBoolean());
        var settled = Settle(test, promise);
        Assert.Equal(PromiseState.Fulfilled, settled.State);
        Assert.True(settled.Result.IsUndefined);
        Assert.Equal(["defer", "adopt", "async", "async resolved", "sync"], log);

        // Already disposed: resolves without disposing again
        Assert.Equal(PromiseState.Fulfilled, Settle(test, Invoke(stack, "disposeAsync")).State);
        Assert.HasCount(5, log);
    }

    [Fact]
    public void AsyncDisposableStack_RejectsWithSuppressedError()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var error1 = test.CreateObject();
        var error2 = test.CreateObject();
        var stack = test.Construct("AsyncDisposableStack");

        // A synchronous [Symbol.dispose] that throws: the error is not thrown synchronously
        Invoke(stack, "use", Disposable(test, WellKnownSymbols.Dispose, Thrower(test, log, "sync", error1)));
        Invoke(stack, "defer", test.CreateFunction((_, _) =>
        {
            log.Add("rejects");
            return test.Call("Promise.reject", error2);
        }));

        var promise = Invoke(stack, "disposeAsync");
        var settled = Settle(test, promise);
        Assert.Equal(["rejects", "sync"], log);
        Assert.Equal(PromiseState.Rejected, settled.State);
        var error = settled.Result.AsObject();
        Assert.Same(error1, Get(error, "error").AsObject());
        Assert.Same(error2, Get(error, "suppressed").AsObject());
    }

    [Fact]
    public void AsyncDisposableStack_SyncDisposeResultIsNotAwaited()
    {
        using var test = new TestRealm();
        var stack = test.Construct("AsyncDisposableStack");
        var pending = (JsPromise)test.Construct("Promise", test.CreateFunction((_, _) => default)).AsObject();
        Invoke(stack, "use", Disposable(test, WellKnownSymbols.Dispose, test.CreateFunction((_, _) => pending)));
        var settled = Settle(test, Invoke(stack, "disposeAsync"));
        Assert.Equal(PromiseState.Fulfilled, settled.State);
        Assert.Equal(PromiseState.Pending, pending.State);
    }

    [Fact]
    public void AsyncDisposableStack_DisposeAsyncRejectsInvalidThis()
    {
        using var test = new TestRealm();
        var promise = test.CallWithThis(test.Construct("DisposableStack"), "AsyncDisposableStack.prototype.disposeAsync");
        var settled = Settle(test, promise);
        Assert.Equal(PromiseState.Rejected, settled.State);
        Assert.Equal("TypeError", Get(settled.Result, "name").AsString());
    }

    [Fact]
    public void AsyncDisposableStack_AwaitsOnceForNull()
    {
        // explicit-await-for-null.js
        using var test = new TestRealm();
        var log = new List<string>();
        var stack = test.Construct("AsyncDisposableStack");
        Invoke(stack, "use", JsValue.Null);
        Invoke(Invoke(test.Call("Promise.resolve"), "then", test.CreateFunction((_, _) => 0)), "then", Logger(test, log, "job 1"));
        Invoke(Invoke(stack, "disposeAsync"), "then", Logger(test, log, "dispose"));
        Invoke(Invoke(test.Call("Promise.resolve"), "then", test.CreateFunction((_, _) => 0)), "then", Logger(test, log, "job 2"));
        test.Engine.RunJobs();
        Assert.Equal(["job 1", "dispose", "job 2"], log);
    }

    [Fact]
    public void AsyncDisposableStack_DoesNotAwaitWhenEmpty()
    {
        // explicit-await-skipped-when-empty.js
        using var test = new TestRealm();
        var log = new List<string>();
        var stack = test.Construct("AsyncDisposableStack");
        Invoke(test.Call("Promise.resolve"), "then", Logger(test, log, "job 1"));
        Invoke(Invoke(stack, "disposeAsync"), "then", Logger(test, log, "dispose"));
        Invoke(test.Call("Promise.resolve"), "then", Logger(test, log, "job 2"));
        test.Engine.RunJobs();
        Assert.Equal(["job 1", "dispose", "job 2"], log);
    }

    [Fact]
    public void AsyncDisposeResourcesOperation_Steps()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var capability = DisposableOperations.NewDisposeCapability();
        DisposableOperations.AddDisposableResource(capability, Disposable(test, WellKnownSymbols.Dispose, Logger(test, log, "sync", 42)), DisposeHint.SyncDispose);
        DisposableOperations.AddDisposableResource(capability, JsValue.Undefined, DisposeHint.AsyncDispose);
        var asyncError = test.CreateObject();
        DisposableOperations.AddDisposableResource(capability, Disposable(test, WellKnownSymbols.AsyncDispose, Logger(test, log, "async", 7)), DisposeHint.AsyncDispose);
        Assert.Equal(3, capability.Count);

        var operation = new AsyncDisposeResourcesOperation(test.Realm, capability, completion: null);

        // The async resource: its result is awaited
        Assert.True(operation.MoveNext(out var value));
        Assert.Equal(7, value.AsNumber());
        Assert.Equal(["async"], log);
        operation.SetAwaitResult(asyncError);

        // hasAwaited is true: no Await(undefined) for the undefined resource before the sync resource
        Assert.False(operation.MoveNext(out _));
        Assert.Equal(["async", "sync"], log);
        Assert.Same(asyncError, operation.Completion!.Value.AsObject());
        Assert.Equal(0, capability.Count);
    }

    [Fact]
    public void AsyncDisposeResourcesOperation_AwaitsForNullBeforeSyncResource()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var capability = DisposableOperations.NewDisposeCapability();
        DisposableOperations.AddDisposableResource(capability, Disposable(test, WellKnownSymbols.Dispose, Logger(test, log, "sync")), DisposeHint.SyncDispose);
        DisposableOperations.AddDisposableResource(capability, JsValue.Null, DisposeHint.AsyncDispose);

        var operation = new AsyncDisposeResourcesOperation(test.Realm, capability, completion: 1);
        Assert.True(operation.MoveNext(out var value));
        Assert.True(value.IsUndefined);
        Assert.Empty(log);
        operation.SetAwaitResult(thrown: null);
        Assert.False(operation.MoveNext(out _));
        Assert.Equal(["sync"], log);
        Assert.Equal(1, operation.Completion!.Value.AsNumber());
    }

    [Fact]
    public void AsyncIteratorPrototype_AsyncDispose()
    {
        using var test = new TestRealm();
        var method = test.Realm.Intrinsics.AsyncIteratorPrototype.Get(new PropertyKey(WellKnownSymbols.AsyncDispose)).AsObject();
        Assert.Equal("[Symbol.asyncDispose]", Get(method, "name").AsString());
        Assert.Equal(0, Get(method, "length").AsNumber());

        var iterator = test.CreateObject();
        var calls = 0;
        iterator.CreateDataPropertyOrThrow(new PropertyKey("return"), test.CreateFunction((thisValue, arguments) =>
        {
            Assert.Same(iterator, thisValue.AsObject());
            Assert.Single(arguments);
            Assert.True(arguments[0].IsUndefined);
            calls++;
            return test.Call("Promise.resolve", 42);
        }));
        var settled = Settle(test, method.Call(iterator, []));
        Assert.Equal(1, calls);
        Assert.Equal(PromiseState.Fulfilled, settled.State);
        Assert.True(settled.Result.IsUndefined);

        // No return method
        Assert.Equal(PromiseState.Fulfilled, Settle(test, method.Call(test.CreateObject(), [])).State);

        // return throws, return rejects, this is undefined
        var error = test.CreateObject();
        var throwing = test.CreateObject();
        throwing.CreateDataPropertyOrThrow(new PropertyKey("return"), test.CreateFunction((_, _) => throw new JsException(error)));
        var rejected = Settle(test, method.Call(throwing, []));
        Assert.Equal(PromiseState.Rejected, rejected.State);
        Assert.Same(error, rejected.Result.AsObject());

        var rejecting = test.CreateObject();
        rejecting.CreateDataPropertyOrThrow(new PropertyKey("return"), test.CreateFunction((_, _) => test.Call("Promise.reject", error)));
        Assert.Same(error, Settle(test, method.Call(rejecting, [])).Result.AsObject());

        Assert.Equal(PromiseState.Rejected, Settle(test, method.Call(JsValue.Undefined, [])).State);
    }
}
