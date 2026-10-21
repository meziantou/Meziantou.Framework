namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class Es2026BuiltinsTests
{
    private static JsValue Get(JsValue target, string name) => JsOperations.GetV(target, new PropertyKey(name));

    private static JsPromise FromAsync(TestRealm test, JsValue thisValue, params JsValue[] arguments)
    {
        var promise = test.Realm.Intrinsics.ArrayFromAsync.Call(thisValue, arguments);
        test.Engine.RunJobs();
        return (JsPromise)promise.AsObject();
    }

    private static JsValue[] ToArray(JsValue array)
    {
        var length = (int)JsOperations.LengthOfArrayLike(array.AsObject());
        var result = new JsValue[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = array.AsObject().Get(new PropertyKey((uint)i));
        }

        return result;
    }

    private static JsValue Resolved(TestRealm test, JsValue value) => test.Call("Promise.resolve", value);

    private static JsValue Rejected(TestRealm test, JsValue value) => test.Call("Promise.reject", value);

    /// <summary>An async iterable whose next() returns a promise of each result.</summary>
    private static JsObject CreateAsyncIterable(TestRealm test, List<string> log, params JsValue[] values)
    {
        var index = 0;
        var iterator = test.CreateObject();
        iterator.CreateDataPropertyOrThrow(new PropertyKey("next"), test.CreateFunction((_, _) =>
        {
            log.Add("next");
            var done = index >= values.Length;
            var value = done ? JsValue.Undefined : values[index++];
            return Resolved(test, test.CreateObject(("value", value), ("done", done)));
        }));
        iterator.CreateDataPropertyOrThrow(new PropertyKey("return"), test.CreateFunction((_, _) =>
        {
            log.Add("return");
            return Resolved(test, test.CreateObject());
        }));
        var iterable = test.CreateObject();
        iterable.CreateDataPropertyOrThrow(new PropertyKey(WellKnownSymbols.AsyncIterator), test.CreateFunction((_, _) => iterator));
        return iterable;
    }

    [Fact]
    public void ErrorIsError()
    {
        using var test = new TestRealm();
        var isError = test.Get("Error.isError").AsObject();
        Assert.Equal(1, Get(isError, "length").AsNumber());
        Assert.Equal("isError", Get(isError, "name").AsString());

        Assert.True(test.Call("Error.isError", test.Construct("Error")).AsBoolean());
        Assert.True(test.Call("Error.isError", test.Construct("TypeError")).AsBoolean());
        Assert.True(test.Call("Error.isError", test.Construct("AggregateError", test.CreateIterable())).AsBoolean());
        Assert.True(test.Call("Error.isError", test.Construct("SuppressedError")).AsBoolean());
        Assert.False(test.Call("Error.isError", test.Get("Error.prototype")).AsBoolean());
        Assert.False(test.Call("Error.isError", new JsObject(test.Realm.Intrinsics.GetErrorPrototype(ErrorType.Error))).AsBoolean());
        Assert.False(test.Call("Error.isError", test.Construct("Proxy", test.Construct("Error"), test.CreateObject())).AsBoolean());
        Assert.False(test.Call("Error.isError", "Error").AsBoolean());
        Assert.False(test.Call("Error.isError").AsBoolean());
    }

    [Fact]
    public void ArrayFromAsync_Properties()
    {
        using var test = new TestRealm();
        var function = test.Realm.Intrinsics.ArrayFromAsync;
        Assert.Equal(1, Get(function, "length").AsNumber());
        Assert.Equal("fromAsync", Get(function, "name").AsString());
        Assert.False(function.IsConstructor);
    }

    [Fact]
    public void ArrayLike_ValuesAreAwaited()
    {
        using var test = new TestRealm();
        var arrayLike = test.CreateObject(("length", 3), ("0", Resolved(test, "a")), ("1", "b"));
        var promise = FromAsync(test, JsValue.Undefined, arrayLike);
        Assert.Equal(PromiseState.Fulfilled, promise.State);
        var array = Assert.IsType<JsArray>(promise.Result.AsObject());
        Assert.Same(test.Realm.Intrinsics.ArrayPrototype, array.GetPrototypeOf());
        Assert.Equal(["a", "b", "undefined"], ToArray(array).Select(value => value.ToString()).ToArray());
    }

    [Fact]
    public void ArrayLike_RejectedElement()
    {
        using var test = new TestRealm();
        var error = test.CreateObject();
        var promise = FromAsync(test, JsValue.Undefined, test.CreateObject(("length", 2), ("0", Rejected(test, error))));
        Assert.Equal(PromiseState.Rejected, promise.State);
        Assert.Same(error, promise.Result.AsObject());
    }

    [Fact]
    public void SyncIterable_ValuesAreAwaited()
    {
        using var test = new TestRealm();
        var iterable = test.CreateIterable(Resolved(test, 1), 2, Resolved(test, 3));
        var promise = FromAsync(test, JsValue.Undefined, iterable);
        Assert.Equal(PromiseState.Fulfilled, promise.State);
        Assert.Equal([1d, 2d, 3d], ToArray(promise.Result).Select(value => value.AsNumber()).ToArray());
        Assert.Equal(0, test.ReturnCount);
    }

    [Fact]
    public void SyncIterable_RejectedValueClosesIterator()
    {
        using var test = new TestRealm();
        var error = test.CreateObject();
        var iterable = test.CreateIterable(1, Rejected(test, error), 3);
        var promise = FromAsync(test, JsValue.Undefined, iterable);
        Assert.Equal(PromiseState.Rejected, promise.State);
        Assert.Same(error, promise.Result.AsObject());
        Assert.Equal(1, test.ReturnCount);
    }

    [Fact]
    public void SyncIterable_MapperThrowsClosesIterator()
    {
        using var test = new TestRealm();
        var error = test.CreateObject();
        var iterable = test.CreateIterable(1, 2);
        var mapper = test.CreateFunction((_, arguments) =>
        {
            Assert.Equal(1, arguments[0].AsNumber());
            Assert.Equal(0, arguments[1].AsNumber());
            throw new JsException(error);
        });
        var promise = FromAsync(test, JsValue.Undefined, iterable, mapper);
        Assert.Equal(PromiseState.Rejected, promise.State);
        Assert.Same(error, promise.Result.AsObject());
        Assert.Equal(1, test.ReturnCount);
    }

    [Fact]
    public void AsyncIterable_MapperIsAwaited()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var thisArg = test.CreateObject();
        var iterable = CreateAsyncIterable(test, log, 1, 2);
        var mapper = test.CreateFunction((thisValue, arguments) =>
        {
            Assert.Same(thisArg, thisValue.AsObject());
            log.Add("map " + arguments[0].ToString() + " " + arguments[1].ToString());
            return Resolved(test, arguments[0].AsNumber() * 10);
        });
        var promise = FromAsync(test, JsValue.Undefined, iterable, mapper, thisArg);
        Assert.Equal(PromiseState.Fulfilled, promise.State);
        Assert.Equal([10d, 20d], ToArray(promise.Result).Select(value => value.AsNumber()).ToArray());
        Assert.Equal(["next", "map 1 0", "next", "map 2 1", "next"], log);
    }

    [Fact]
    public void AsyncIterable_MapperRejectsClosesIterator()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var error = test.CreateObject();
        var iterable = CreateAsyncIterable(test, log, 1, 2);
        var promise = FromAsync(test, JsValue.Undefined, iterable, test.CreateFunction((_, _) => Rejected(test, error)));
        Assert.Equal(PromiseState.Rejected, promise.State);
        Assert.Same(error, promise.Result.AsObject());
        Assert.Equal(["next", "return"], log);
    }

    [Fact]
    public void AsyncIterable_NextRejectsDoesNotClose()
    {
        using var test = new TestRealm();
        var error = test.CreateObject();
        var log = new List<string>();
        var iterator = test.CreateObject();
        iterator.CreateDataPropertyOrThrow(new PropertyKey("next"), test.CreateFunction((_, _) => Rejected(test, error)));
        iterator.CreateDataPropertyOrThrow(new PropertyKey("return"), test.CreateFunction((_, _) =>
        {
            log.Add("return");
            return test.CreateObject();
        }));
        var iterable = test.CreateObject();
        iterable.CreateDataPropertyOrThrow(new PropertyKey(WellKnownSymbols.AsyncIterator), test.CreateFunction((_, _) => iterator));
        var promise = FromAsync(test, JsValue.Undefined, iterable);
        Assert.Equal(PromiseState.Rejected, promise.State);
        Assert.Same(error, promise.Result.AsObject());
        Assert.Empty(log);
    }

    [Fact]
    public void Errors_RejectThePromise()
    {
        using var test = new TestRealm();
        var notCallable = FromAsync(test, JsValue.Undefined, test.CreateArray(), 1);
        Assert.Equal(PromiseState.Rejected, notCallable.State);
        Assert.Equal("TypeError", Get(notCallable.Result, "name").AsString());

        var nullItems = FromAsync(test, JsValue.Undefined, JsValue.Null);
        Assert.Equal(PromiseState.Rejected, nullItems.State);
        Assert.Equal("TypeError", Get(nullItems.Result, "name").AsString());

        var tooLong = FromAsync(test, JsValue.Undefined, test.CreateObject(("length", 4294967296d)));
        Assert.Equal(PromiseState.Rejected, tooLong.State);
        Assert.Equal("RangeError", Get(tooLong.Result, "name").AsString());

        var badIterator = test.CreateObject();
        badIterator.CreateDataPropertyOrThrow(new PropertyKey(WellKnownSymbols.Iterator), 1);
        var badIteratorResult = FromAsync(test, JsValue.Undefined, badIterator);
        Assert.Equal(PromiseState.Rejected, badIteratorResult.State);
    }

    [Fact]
    public void ThisConstructor()
    {
        using var test = new TestRealm();
        var log = new List<string>();
        var created = new List<JsObject>();
        var constructor = test.Realm.CreateBuiltinFunction("MyArray", 0, (_, _, arguments, _) =>
        {
            log.Add("construct " + arguments.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + (arguments.Length > 0 ? " " + arguments[0].ToString() : ""));
            var obj = test.CreateObject();
            created.Add(obj);
            return obj;
        }, isConstructor: true);

        var fromIterable = FromAsync(test, constructor, test.CreateIterable(1, 2));
        Assert.Equal(PromiseState.Fulfilled, fromIterable.State);
        Assert.Same(created[0], fromIterable.Result.AsObject());
        Assert.Equal(2, Get(fromIterable.Result, "length").AsNumber());
        Assert.Equal(2, Get(fromIterable.Result, "1").AsNumber());

        var fromArrayLike = FromAsync(test, constructor, test.CreateObject(("length", 1), ("0", "x")));
        Assert.Equal(PromiseState.Fulfilled, fromArrayLike.State);
        Assert.Equal(1, Get(fromArrayLike.Result, "length").AsNumber());
        Assert.Equal(["construct 0", "construct 1 1"], log);
    }

    [Fact]
    public void ThisConstructor_UnsettableElementClosesIterator()
    {
        using var test = new TestRealm();
        var constructor = test.Realm.CreateBuiltinFunction("Frozen", 0, (_, _, _, _) =>
        {
            var obj = test.CreateObject();
            obj.PreventExtensions();
            return obj;
        }, isConstructor: true);

        var promise = FromAsync(test, constructor, test.CreateIterable(1, 2));
        Assert.Equal(PromiseState.Rejected, promise.State);
        Assert.Equal("TypeError", Get(promise.Result, "name").AsString());
        Assert.Equal(1, test.ReturnCount);
    }

    [Fact]
    public void SyncIterable_AwaitsEachValueOnce()
    {
        // sync-iterable-with-thenable-awaits-once.js
        using var test = new TestRealm();
        var expected = test.CreateObject();
        var calls = 0;
        var thenable = test.CreateObject();
        thenable.CreateDataPropertyOrThrow(new PropertyKey("then"), test.CreateFunction((_, arguments) =>
        {
            calls++;
            return JsOperations.Call(arguments[0], JsValue.Undefined, [expected]);
        }));

        var promise = FromAsync(test, JsValue.Undefined, test.CreateIterable(thenable));
        Assert.Equal(PromiseState.Fulfilled, promise.State);
        Assert.Equal(1, calls);
        Assert.Same(expected, ToArray(promise.Result)[0].AsObject());
    }
}
