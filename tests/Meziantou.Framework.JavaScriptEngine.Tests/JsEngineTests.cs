using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Meziantou.Framework.JavaScriptEngine.Samples;

namespace Meziantou.Framework.JavaScriptEngine.Tests;

public sealed class JsEngineTests
{
    [Fact]
    public void CreateAndDispose()
    {
        using var engine = new JsEngine();
        Assert.NotNull(engine);
    }

    [Fact]
    public void ObjectOperations()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var obj = realm.CreateObject();
        Assert.True(obj.SetProperty("a", 1));
        Assert.True(obj.DefineProperty("b", JsPropertyDescriptor.Data(2, writable: false)));
        Assert.False(obj.SetProperty("b", 3));
        Assert.Equal(2, obj.GetProperty("b").AsNumber());
        Assert.True(obj.TryGetOwnProperty("b", out var descriptor));
        Assert.True(descriptor.IsDataDescriptor);
        Assert.False(descriptor.Writable);
        Assert.True(obj.ContainsProperty("toString"));
        Assert.Equal(["a", "b"], obj.GetOwnPropertyKeys().Select(key => key.AsString()));
        Assert.True(obj.DeleteProperty("a"));
        Assert.Same(realm.ObjectPrototype, obj.GetPrototype());
        Assert.True(obj.SetPrototype(null));
        Assert.Null(obj.GetPrototype());

        realm.GlobalObject.SetProperty("obj", obj);
        Assert.Equal("undefined,2", realm.Execute("String(obj.a) + ',' + obj.b").AsString());
    }

    [Fact]
    public void LazyProperties()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var created = 0;
        realm.GlobalObject.DefineLazyDataProperty("lazy", () => { created++; return 42; }, writable: true, enumerable: false, configurable: true);
        realm.GlobalObject.DefineLazyAccessorProperty("lazyGetter", () => { created++; return realm.CreateFunction("get lazyGetter", 0, (_, _) => "value"); }, setterFactory: null, enumerable: false, configurable: true);
        Assert.Equal(0, created);
        Assert.Equal("42,value", realm.Execute("lazy + ',' + lazyGetter").AsString());
        Assert.Equal(2, created);
    }

    // The built-in methods and accessors, and some built-ins, are created on first use: it is not observable
    [Theory]
    [InlineData("var names = Object.getOwnPropertyNames(globalThis); var order = ['Map', 'Set', 'WeakMap', 'WeakSet', 'WeakRef', 'FinalizationRegistry', 'Promise', 'RegExp', 'ArrayBuffer', 'Date', 'SuppressedError', 'DisposableStack', 'AsyncDisposableStack'].map(n => names.indexOf(n)); order.every((index, i) => index > 0 && (i === 0 || index > order[i - 1]))", "true")]
    [InlineData("delete globalThis.RegExp; 'abc'.match(/b/)[0] + 'a-b'.split(/-/).length", "b2")]
    [InlineData("delete globalThis.Map; typeof Map + '|' + Object.prototype.toString.call(new Set().entries())", "undefined|[object Set Iterator]")]
    [InlineData("delete globalThis.Set; new Map([[1, 2]]).size + Object.getPrototypeOf(new Map().keys())[Symbol.toStringTag]", "1Map Iterator")]
    [InlineData("new Date(0).toLocaleDateString('en', { timeZone: 'UTC' }) + '|' + Object.getOwnPropertyNames(Date.prototype).includes('toLocaleTimeString')", "1/1/1970|true")]
    [InlineData("typeof new WeakRef({}).deref() + '|' + typeof new FinalizationRegistry(() => {}).register", "object|function")]
    [InlineData("var r = new ShadowRealm(); [Object.prototype.toString.call(r), r.evaluate('var x = 1; x + 1'), r.evaluate('typeof x'), typeof x].join()", "[object ShadowRealm],2,number,undefined")]
    [InlineData("typeof new DisposableStack().use + '|' + typeof AsyncDisposableStack.prototype.disposeAsync + '|' + new SuppressedError(1, 2).suppressed", "function|function|2")]
    [InlineData("var d = Object.getOwnPropertyDescriptor(Array.prototype, 'map'); [typeof d.value, d.value.name, d.value.length, d.writable, d.enumerable, d.configurable, d.value === [].map].join()", "function,map,1,true,false,true,true")]
    [InlineData("var d = Object.getOwnPropertyDescriptor(Map.prototype, 'size'); [typeof d.get, d.get.name, d.get.length, d.set, d.get === Object.getOwnPropertyDescriptor(Map.prototype, 'size').get].join()", "function,get size,0,,true")]
    [InlineData("Object.freeze(String.prototype); 'abc'.at(-1) + Object.isFrozen(String.prototype) + Object.getOwnPropertyDescriptor(String.prototype, 'at').writable", "ctruefalse")]
    [InlineData("delete globalThis.Promise; var p = (async () => 1)(); [Object.getPrototypeOf(p).constructor.name, typeof p.then, Object.getPrototypeOf(p) === Object.getPrototypeOf((async () => {})())].join()", "Promise,function,true")]
    [InlineData("var g = function* () { yield 1; }; var proto = Object.getPrototypeOf(g); [proto[Symbol.toStringTag], proto.constructor.name, [...g()].join(), Object.getOwnPropertyNames(Object.getPrototypeOf(g.prototype)).join()].join('|')", "GeneratorFunction|GeneratorFunction|1|constructor,next,return,throw")]
    [InlineData("var asyncIteratorPrototype = Object.getPrototypeOf(Object.getPrototypeOf(async function* () { }.prototype)); Reflect.ownKeys(asyncIteratorPrototype).map(String).join() + '|' + Object.getPrototypeOf(async function () { }).constructor.name", "Symbol(Symbol.asyncIterator),Symbol(Symbol.asyncDispose)|AsyncFunction")]
    [InlineData("Object.getPrototypeOf([1].values().map(x => x))[Symbol.toStringTag] + '|' + Object.getOwnPropertyNames(Object.getPrototypeOf(Iterator.from({ next() { } }))).join()", "Iterator Helper|next,return")]
    public void LazyBuiltins(string code, string expected)
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var result = realm.Execute(code);
        using var scope = engine.Enter();
        Assert.Equal(expected, JsOperations.ToString(result));
    }

    [Fact]
    public void ShadowRealmsAreNotAddedToTheRealmsOfTheEngine()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var result = realm.Execute("var r = new ShadowRealm(); var add = r.evaluate('(a, b) => a + b'); add(1, 2)");
        using var scope = engine.Enter();
        Assert.Equal("3", JsOperations.ToString(result));
        Assert.Single(engine.Realms);
    }

    [Fact]
    public void BuiltinMethodTablesAreSharedByTheRealms()
    {
        // The realms share the static tables of the built-in methods, and the sizes they learn: each realm still gets its own
        // function objects, and the properties of a realm do not depend on what the scripts of another realm added
        using var engine = new JsEngine();
        var realm1 = engine.CreateRealm();
        const string Names = "[Object.getOwnPropertyNames(Array.prototype).join(), Object.getOwnPropertyNames(Math).join(), Object.getOwnPropertyNames(String.prototype).length].join('|')";
        var names1 = realm1.Execute(Names);
        realm1.Execute("for (var i = 0; i < 100; i++) Array.prototype['extra' + i] = i;");
        var realm2 = engine.CreateRealm();
        var names2 = realm2.Execute(Names);
        realm2.GlobalObject.SetProperty("otherMap", realm1.Execute("Array.prototype.map"));
        using var scope = engine.Enter();
        Assert.Equal(JsOperations.ToString(names1), JsOperations.ToString(names2));
        Assert.Equal("false,2,4,true", JsOperations.ToString(realm2.Execute("[otherMap === Array.prototype.map, [1].map(x => x * 2)[0], otherMap.call([2], x => x * 2)[0], 'extra0' in Array.prototype === false].join()")));
    }

    [Fact]
    public void MakePrototypeImmutable()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var prototype = realm.CreateObject();
        var obj = realm.CreateObject();
        Assert.True(obj.SetPrototype(prototype));
        obj.MakePrototypeImmutable();
        Assert.True(obj.SetPrototype(prototype));
        Assert.False(obj.SetPrototype(null));
        Assert.Same(prototype, obj.GetPrototype());
        realm.GlobalObject.SetProperty("obj", obj);
        Assert.Equal("TypeError,false,true", realm.Execute("""
            var results = [];
            try { Object.setPrototypeOf(obj, {}); } catch (e) { results.push(e.name); }
            results.push(Reflect.setPrototypeOf(obj, null), Reflect.setPrototypeOf(obj, Object.getPrototypeOf(obj)));
            results.join()
            """).AsString());
    }

    [Fact]
    public void SetGlobalThis()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var thisValue = realm.CreateObject();
        realm.SetGlobalThis(thisValue);
        realm.GlobalObject.SetProperty("thisValue", thisValue);
        Assert.Same(thisValue, realm.GlobalThis.TryGetObject());

        // The global object keeps the global bindings: only the this value of the global code changes
        Assert.Equal("true,true,true,true,true,true,false,1,undefined", realm.Execute("""
            var declared = 1;
            function sloppy() { return this; }
            function strict() { 'use strict'; return this; }
            [
              this === thisValue, globalThis === thisValue, sloppy() === thisValue, (0, eval)('this') === thisValue,
              Function('return this')() === thisValue, (() => this)() === thisValue, strict() === thisValue,
              declared, typeof thisValue.declared,
            ].join()
            """).AsString());

        Assert.True(realm.GlobalObject.TryGetOwnProperty("globalThis", out var descriptor));
        Assert.Same(thisValue, descriptor.Value.TryGetObject());
        Assert.True(descriptor.Writable);
        Assert.False(descriptor.Enumerable);
        Assert.True(descriptor.Configurable);
    }

    [Theory]
    [InlineData("0", true, 0u)]
    [InlineData("4294967294", true, 4294967294u)]
    [InlineData("4294967295", false, 0u)]
    [InlineData("01", false, 0u)]
    [InlineData("-0", false, 0u)]
    [InlineData("a", false, 0u)]
    public void PropertyKey_TryGetArrayIndex(string name, bool isIndex, uint expectedIndex)
    {
        var key = new JsPropertyKey(name);
        Assert.Equal(isIndex, key.TryGetArrayIndex(out var index));
        Assert.Equal(expectedIndex, index);
    }

    [Fact]
    public void ReserveProperties()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var obj = realm.CreateObject();
        obj.SetProperty("a", 1);
        obj.ReserveProperties(100);
        for (var i = 0; i < 100; i++)
        {
            obj.SetProperty("p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), i);
        }

        realm.GlobalObject.SetProperty("obj", obj);
        Assert.Equal("101,1,99", realm.Execute("[Object.keys(obj).length, obj.a, obj.p99].join()").AsString());
    }

    [Fact]
    public void FunctionsCallsAndConstructors()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", 2, (_, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
        realm.GlobalObject.SetProperty("Point", realm.CreateConstructor("Point", 0, (_, _, newTarget) => newTarget is null ? "called" : "constructed"));
        Assert.Equal("3,2,add,called", realm.Execute("add(1, 2) + ',' + add.length + ',' + add.name + ',' + Point()").AsString());

        var function = realm.Execute("(function (a) { return this.x + a; })");
        var thisValue = realm.CreateObject();
        thisValue.SetProperty("x", 40);
        Assert.Equal(42, engine.Call(function, thisValue, 2).AsNumber());

        var classes = realm.Execute("class A { constructor() { this.target = new.target.name; } } class B extends A {} [A, B]").AsObject();
        var instance = engine.Construct(classes.GetProperty("0"), [], classes.GetProperty("1")).AsObject();
        Assert.Equal("B", instance.GetProperty("target").AsString());

        var exception = Assert.Throws<JsException>(() => engine.Call(JsValue.Undefined, JsValue.Undefined));
        Assert.StartsWith("TypeError", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversions()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var value = realm.Execute("({ valueOf() { return 42; }, toString() { return 'text'; } })");
        Assert.Equal(42, engine.ToNumber(value));
        Assert.Equal("text", engine.ToString(value));
        Assert.True(JsEngine.ToBoolean(value));
        Assert.Equal(12, engine.ToBigInt(realm.Execute("12n")));
        Assert.True(engine.IsArray(realm.Execute("new Proxy([], {})")));
        Assert.True(JsEngine.SameValue(double.NaN, double.NaN));
        Assert.Equal("1", engine.ToPropertyKey(1).AsString());
    }

    [Fact]
    public void ExceptionsHaveTheirLocation()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var exception = Assert.Throws<JsException>(() => realm.Execute("var a = 1;\n  throw new Error('boom');", "script.js"));
        Assert.Equal(new JsSourceLocation("script.js", 2, 3), exception.Location);

        exception = Assert.Throws<JsException>(() => realm.Execute("var a = 1;\nnull.x;", "script.js"));
        Assert.Equal("script.js", exception.Location?.Url);
        Assert.Equal(2, exception.Location?.Line);

        using var scope = engine.Enter();
        var error = realm.CreateError(JsErrorType.RangeError, "out of range");
        Assert.True(error.HasInternalSlot(JsInternalSlot.ErrorData));
        Assert.Equal("RangeError: out of range", error.GetErrorStack());
        Assert.True(error.TrySetErrorStack("custom"));
        realm.GlobalObject.SetProperty("error", error);
        Assert.Equal("custom", realm.Execute("error.stack").AsString());
    }

    [Fact]
    public void PreparedScriptRunsInSeveralEngines()
    {
        var script = JsPreparedScript.Parse("var counter = (globalThis.counter ?? 0) + 1; counter", "prepared.js", lineOffset: 9, columnOffset: 4);
        for (var i = 0; i < 2; i++)
        {
            using var engine = new JsEngine();
            var realm = engine.CreateRealm();
            Assert.Equal(1, realm.Execute(script).AsNumber());
            Assert.Equal(2, realm.Execute(script).AsNumber());
        }

        var thrower = JsPreparedScript.Parse("throw new Error('x')", "inline.html", lineOffset: 9, columnOffset: 4);
        using var other = new JsEngine();
        var exception = Assert.Throws<JsException>(() => other.CreateRealm().Execute(thrower));
        Assert.Equal(new JsSourceLocation("inline.html", 10, 5), exception.Location);

        Assert.False(JsPreparedScript.TryParse("var = 1", "invalid.js", 0, 0, out _, out var error));
        Assert.Equal("invalid.js", error.Url);
    }

    [Fact]
    public void PreparedScriptWithUnboundFunctionName()
    {
        const string Prefix = "var onload = 'global'; ({ onload: ";
        const string Code = Prefix + "function onload() { return typeof onload; } }).onload";
        var script = JsPreparedScript.Parse(Code, "handler.js", 0, 0, unboundNameFunctionStart: Prefix.Length);
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.GlobalObject.SetProperty("f", realm.Execute(script));
        Assert.Equal("string", realm.Execute("f()").AsString());
        Assert.Equal("onload", realm.Execute("f.name").AsString());
        Assert.Equal("function onload() { return typeof onload; }", realm.Execute("f.toString()").AsString());

        var bound = JsPreparedScript.Parse(Code, "handler.js", 0, 0, unboundNameFunctionStart: -1);
        realm.GlobalObject.SetProperty("g", realm.Execute(bound));
        Assert.Equal("function", realm.Execute("g()").AsString());

        Assert.True(JsPreparedScript.TryParse(Code, "handler.js", 0, 0, Prefix.Length, out var parsed, out _));
        Assert.Equal("handler.js", parsed.Url);
        Assert.False(JsPreparedScript.TryParse("var = 1", "invalid.js", 0, 0, Prefix.Length, out _, out var error));
        Assert.Equal("invalid.js", error.Url);
    }

    [Fact]
    public void Jobs()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var log = new List<string>();
        engine.EnqueueJob(() => log.Add("job"));
        using (engine.EnterNestedJobQueue())
        {
            engine.EnqueueJob(() => log.Add("nested"));
            engine.RunJobs();
        }

        Assert.Equal(["nested"], log);
        engine.RunJobs();
        Assert.Equal(["nested", "job"], log);

        var promise = realm.Execute("Promise.reject(1)").AsObject();
        Assert.True(promise.TryGetPromiseState(out var state, out var result, out var isHandled));
        Assert.Equal(JsPromiseState.Rejected, state);
        Assert.Equal(1, result.AsNumber());
        Assert.False(isHandled);
    }

    [Fact]
    public void RunInRealm()
    {
        using var engine = new JsEngine();
        var first = engine.CreateRealm();
        var second = engine.CreateRealm();
        var array = engine.RunInRealm(second, () => second.Execute("[]").AsObject());
        Assert.Same(first, engine.RunInRealm(first, () => engine.CurrentRealm));
        first.Execute("1");
        using var scope = engine.Enter();
        Assert.NotSame(first.GlobalObject.GetProperty("Array").AsObject().GetProperty("prototype").AsObject(), array.GetPrototype());

        // A function runs in its own realm, or in the realm of its caller
        JsRealm? calledRealm = null;
        second.GlobalObject.SetProperty("ownRealm", second.CreateFunction("ownRealm", 0, (_, _) => { calledRealm = engine.CurrentRealm; return default; }));
        second.GlobalObject.SetProperty("callerRealm", second.CreateFunction("callerRealm", 0, (_, _) => { calledRealm = engine.CurrentRealm; return default; }, runInCallerRealm: true));
        first.GlobalObject.SetProperty("other", second.GlobalObject);
        first.Execute("other.ownRealm()");
        Assert.Same(second, calledRealm);
        first.Execute("other.callerRealm()");
        Assert.Same(first, calledRealm);
    }

    [Fact]
    public void HostObjectsOverrideTheInternalMethods()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var target = new object();
        var host = new UppercaseHostObject(realm.ObjectPrototype, target);
        Assert.Same(target, host.Target);
        realm.GlobalObject.SetProperty("host", host);
        Assert.Equal("NAME,real,true,false", realm.Execute("host.real = 'real'; host.name + ',' + host.real + ',' + ('name' in host) + ',' + Object.keys(host).includes('name')").AsString());

        host.IsHtmlDdaObject = true;
        Assert.Equal("undefined,true", realm.Execute("typeof host + ',' + (host == null)").AsString());
    }

    [Fact]
    public void HostObjectsCanBeCallable()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var host = new CallableHostObject(realm.ObjectPrototype);
        realm.GlobalObject.SetProperty("host", host);
        Assert.Equal("object", realm.Execute("typeof host").AsString());
        Assert.Throws<JsException>(() => realm.Execute("host()"));

        host.HasCallInternalMethod = true;
        Assert.True(host.IsCallable);
        Assert.False(host.IsConstructor);
        Assert.Equal("function,called 2,called 1,true", realm.Execute("typeof host + ',' + host(1, 2) + ',' + Reflect.apply(host, null, ['x']) + ',' + (Function.prototype.toString.call(host) === 'function () { [native code] }')").AsString());
        Assert.Throws<JsException>(() => realm.Execute("new host()"));

        host.IsHtmlDdaObject = true;
        Assert.Equal("undefined,false,true,called 0", realm.Execute("typeof host + ',' + Boolean(host) + ',' + (host == undefined) + ',' + host()").AsString());
    }

    [Fact]
    public void BuffersShareTheirMemory()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var external = new byte[] { 1, 2, 3, 4 };
        var buffer = realm.CreateArrayBuffer(external.AsMemory(), "key");
        var view = realm.CreateBufferView(JsBufferKind.Uint8Array, buffer, 1, 2);
        realm.GlobalObject.SetProperty("view", view);
        realm.Execute("view[0] = 42");
        Assert.Equal(42, external[1]);

        Assert.True(JsBinaryData.TryGetBufferSource(view, out var kind, out var isShared, out var bytes));
        Assert.Equal(JsBufferKind.Uint8Array, kind);
        Assert.False(isShared);
        Assert.Equal([42, 3], bytes.ToArray());

        Assert.Throws<JsException>(() => realm.Execute("view.buffer.transfer()"));
        JsBinaryData.DetachArrayBuffer(buffer, "key");
        Assert.Equal(0, realm.Execute("view.length").AsNumber());

        var shared = realm.Execute("new SharedArrayBuffer(8)");
        var block = JsBinaryData.GetSharedDataBlock(shared);
        Assert.NotNull(block);
        using var otherEngine = new JsEngine();
        var otherRealm = otherEngine.CreateRealm();
        using (otherEngine.Enter())
        {
            otherRealm.GlobalObject.SetProperty("shared", otherRealm.CreateSharedArrayBuffer(block));
        }

        otherRealm.Execute("new Uint8Array(shared)[0] = 7");
        Assert.Equal(7, block.GetSpan()[0]);

        Assert.True(realm.CreateDate(0).TryGetDateValue(out var timeValue));
        Assert.Equal(0, timeValue);
        Assert.Equal([5, 6], JsBinaryData.TryGetBufferSource(realm.CreateArrayBuffer([5, 6]), out _, out _, out var copy) ? copy.ToArray() : []);
    }

    [Fact]
    public void BufferViewsCreatedByTheHostAreAlignedAndInBounds()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        var shared = realm.Execute("new SharedArrayBuffer(32)").AsObject();

        // The atomic operations access the elements as aligned integers
        Assert.Throws<ArgumentException>("byteOffset", () => realm.CreateBufferView(JsBufferKind.BigInt64Array, shared, 12, 1));
        Assert.Throws<ArgumentException>("byteOffset", () => realm.CreateBufferView(JsBufferKind.Int32Array, shared, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>("byteOffset", () => realm.CreateBufferView(JsBufferKind.Uint8Array, shared, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>("length", () => realm.CreateBufferView(JsBufferKind.BigInt64Array, shared, 24, 2));
        Assert.Throws<ArgumentOutOfRangeException>("length", () => realm.CreateBufferView(JsBufferKind.DataView, shared, 30, 3));

        // A DataView has no alignment
        _ = realm.CreateBufferView(JsBufferKind.DataView, shared, 3, 29);

        realm.GlobalObject.SetProperty("view", realm.CreateBufferView(JsBufferKind.BigInt64Array, shared, 8, 3));
        Assert.Equal("0,5", realm.Execute("String(Atomics.add(view, 2, 5n)) + ',' + String(Atomics.load(view, 2))").AsString());
    }

    [Fact]
    public void HostHooks()
    {
        var host = new RecordingHost();
        using var engine = new JsEngine(new JsEngineOptions { Host = host });
        var realm = engine.CreateRealm();
        realm.Execute("var p = Promise.reject(1); p.catch(() => {}); eval('1 + 1'); new Function('a', 'return a');");
        engine.RunJobs();
        Assert.Equal([JsPromiseRejectionOperation.Reject, JsPromiseRejectionOperation.Handle], host.Rejections);
        Assert.Equal(["IndirectEval|1 + 1", "Function|return a"], host.Compilations.Select(c => c.Replace("DirectEval", "IndirectEval", StringComparison.Ordinal)));
        Assert.Equal(1, host.Callbacks);
    }

    [Fact]
    public void HostHooks_AwaitContinuationRunsInTheRealmOfTheAsyncFunction()
    {
        // The awaited promise is rejected by another realm: the promise of the async function is rejected in its own realm
        var host = new RecordingHost();
        using var engine = new JsEngine(new JsEngineOptions { Host = host });
        var otherRealm = engine.CreateRealm();
        var functionRealm = engine.CreateRealm();
        functionRealm.GlobalObject.SetProperty("p", otherRealm.Execute("var reject; new Promise((_, r) => reject = r)"));
        functionRealm.Execute("(async () => { await p; })();");
        otherRealm.Execute("reject(1);");
        engine.RunJobs();
        Assert.Equal(JsPromiseRejectionOperation.Reject, host.Rejections[^1]);
        Assert.Same(functionRealm, host.RejectionRealms[^1]);
    }

    [Fact]
    public void HostCodeForEval_IsUsedByEvalAndTheFunctionConstructor()
    {
        using var engine = new JsEngine(new JsEngineOptions { Host = new TrustedCodeHost() });
        var realm = engine.CreateRealm();

        // The code of a trusted object is its code, not the result of its toString method
        Assert.Equal("15,4,0", realm.Execute("""
            let calls = 0;
            const trusted = code => ({ trustedCode: code, toString() { calls++; return 'forged'; } });
            const f = new Function(trusted('a'), trusted('b'), trusted('return a * b'));
            [f(3, 5), eval(trusted('2 + 2')), calls].join()
            """).AsString());
    }

    [Fact]
    public void ActiveScriptOrModule()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        string? url = null;
        JsSourceLocation? location = null;
        realm.GlobalObject.SetProperty("probe", realm.CreateFunction("probe", 0, (_, _) =>
        {
            url = engine.GetActiveScriptOrModule()?.Url;
            location = engine.GetCurrentLocation();
            return default;
        }));
        realm.Execute("\n  probe();", "active.js");
        Assert.Equal("active.js", url);
        Assert.Equal(new JsSourceLocation("active.js", 2, 3), location);

        // The location of a call in the concise body of an arrow function is the location of the call
        realm.Execute("[0].forEach(_ =>\n    probe());", "arrow.js");
        Assert.Equal(new JsSourceLocation("arrow.js", 2, 5), location);

        // The location of a method call is the location of the name of the method
        realm.Execute("var o = { m: probe };\no.\n  m();", "method.js");
        Assert.Equal(new JsSourceLocation("method.js", 3, 3), location);
        Assert.Null(engine.GetActiveScriptOrModule());

        // The script of a dynamic function is the script that created it, not the script that calls it
        realm.Execute("var dynamic = new Function('probe()'); var dynamicGenerator = new (function* () {}).constructor('probe()');", "creator.js");
        realm.Execute("dynamic();", "caller.js");
        Assert.Equal("creator.js", url);
        realm.Execute("dynamicGenerator().next();", "caller.js");
        Assert.Equal("creator.js", url);

        // A dynamic function created by the host has no script: the script of its caller is the active one
        realm.GlobalObject.SetProperty("hostDynamic", engine.Construct(realm.Execute("Function"), ["probe()"]));
        realm.Execute("hostDynamic();", "caller.js");
        Assert.Equal("caller.js", url);
    }

    [Fact]
    public void ExecutionLimits()
    {
        using (var engine = new JsEngine(new JsEngineOptions { MaxStatements = 100 }))
        {
            var realm = engine.CreateRealm();
            Assert.Equal(10, realm.Execute("var i = 0; while (i < 10) i++; i").AsNumber());
            var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("for (;;) {}"));
            Assert.Equal(JsTerminationReason.StatementLimit, exception.Reason);

            // A recursion without loops counts its calls; the limit applies to each entry
            exception = Assert.Throws<JsTerminationException>(() => realm.Execute("function f(n) { return n < 2 ? n : f(n - 1) + f(n - 2); } f(30)"));
            Assert.Equal(JsTerminationReason.StatementLimit, exception.Reason);
            Assert.Equal(3, realm.Execute("1 + 2").AsNumber());

            // An endless recursion of tail calls never exceeds the recursion limit: it is a loop
            exception = Assert.Throws<JsTerminationException>(() => realm.Execute("'use strict'; function g() { return g(); } g()"));
            Assert.Equal(JsTerminationReason.StatementLimit, exception.Reason);
        }

        using (var engine = new JsEngine(new JsEngineOptions { RegExpTimeout = TimeSpan.FromMilliseconds(50) }))
        {
            var realm = engine.CreateRealm();
            Assert.True(realm.Execute("/^(a+)+$/.test('aaaa')").AsBoolean());
            var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("/^(a+)+$/.test('a'.repeat(40) + 'b')"));
            Assert.Equal(JsTerminationReason.Timeout, exception.Reason);
        }

        using (var engine = new JsEngine(new JsEngineOptions { MemoryLimit = 1024 * 1024 }))
        {
            var exception = Assert.Throws<JsTerminationException>(() => engine.CreateRealm().Execute("var a = []; for (;;) a.push({});"));
            Assert.Equal(JsTerminationReason.MemoryLimit, exception.Reason);
        }

        using var cancellation = new CancellationTokenSource();
        using (var engine = new JsEngine(new JsEngineOptions { CancellationToken = cancellation.Token }))
        {
            var realm = engine.CreateRealm();
            using var scope = engine.Enter();
            engine.CheckInterrupt();
            realm.GlobalObject.SetProperty("check", realm.CreateFunction("check", 0, (_, _) =>
            {
                cancellation.Cancel();
                engine.CheckInterrupt();
                return default;
            }));
            var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("check()"));
            Assert.Equal(JsTerminationReason.Canceled, exception.Reason);
        }
    }

    // A built-in function has no interrupt point: it checks the memory limit before a large allocation, and while its
    // result grows. Without these checks, each script allocates gigabytes.
    [Theory]
    [InlineData("'abc'.padStart(933886059)")]
    [InlineData("'abc'.padEnd(933886059, 'xy')")]
    [InlineData("'abc'.repeat(300000000)")]
    [InlineData("try { 'a'.repeat(1e9) } catch { }")]
    [InlineData("let s = 'a'; for (let i = 0; i < 29; i++) s += s; s.at(-1)")]
    [InlineData("'a'.repeat(1 << 16).replaceAll('a', 'b'.repeat(400))")]
    [InlineData("'a'.repeat(1 << 16).replace(/a/g, '$&'.repeat(200))")]
    [InlineData("Array(1e7).join('x'.repeat(100))")]
    [InlineData("Array(1e8).join('x')")]
    [InlineData("new Uint8Array(1e8).join('x'.repeat(100))")]
    [InlineData("JSON.stringify(Array(1e8))")]
    [InlineData("JSON.stringify('\\u0001'.repeat(1 << 22))")]
    [InlineData("String.raw({ raw: Array(1e7).fill('x'.repeat(100)) })")]
    [InlineData("escape('\\u0100'.repeat(1 << 22))")]
    [InlineData("encodeURIComponent('\\u0800'.repeat(1 << 22))")]
    [InlineData("'x'.link('\"'.repeat(1 << 22))")]
    [InlineData("'\\uFDFA'.repeat(1 << 20).normalize('NFKD')")]
    [InlineData("Array.from({ length: 1e8 })")]
    [InlineData("Array(1e8).fill(0)")]
    [InlineData("new ArrayBuffer(2e9)")]
    [InlineData("new Float64Array(2.5e8)")]
    [InlineData("Object.keys('a'.repeat(1 << 22))")]
    public void ExecutionLimits_MemoryLimitStopsBuiltins(string code)
    {
        using var engine = new JsEngine(new JsEngineOptions { MemoryLimit = 16 * 1024 * 1024 });
        var exception = Assert.Throws<JsTerminationException>(() => engine.CreateRealm().Execute(code));
        Assert.Equal(JsTerminationReason.MemoryLimit, exception.Reason);
    }

    [Fact]
    public void ExecutionLimits_BuiltinsWithinTheLimits()
    {
        using var engine = new JsEngine(new JsEngineOptions { MemoryLimit = 64 * 1024 * 1024 });
        var realm = engine.CreateRealm();
        Assert.Equal(1 << 22, realm.Execute("'a'.repeat(1 << 22).normalize('NFKD').length").AsNumber());
        Assert.Equal((1 << 20) + 2, realm.Execute("('a'.repeat(1 << 20) + '\\u00E9').normalize('NFD').length").AsNumber());
        Assert.Equal(1 << 22, realm.Execute("'ab'.repeat(1 << 20).padEnd(1 << 22, 'xy').length").AsNumber());
        Assert.Equal((1 << 20) * 2, realm.Execute("'a'.repeat(1 << 20).replaceAll('a', 'bc').length").AsNumber());
        Assert.Equal(((1 << 18) * 5) - 2, realm.Execute("Array(1 << 18).fill('abc').join(', ').length").AsNumber());
        Assert.Equal(1 << 20, realm.Execute("let s = 'a'; for (let i = 0; i < 20; i++) s += s; s.length").AsNumber());
    }

    [Fact]
    public void ExecutionLimits_TimeoutStopsBuiltins()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromMilliseconds(200) });
        var exception = Assert.Throws<JsTerminationException>(() => engine.CreateRealm().Execute("Array.prototype.indexOf.call(new Proxy({ length: 1e9 }, {}), 1)"));
        Assert.Equal(JsTerminationReason.Timeout, exception.Reason);
    }

    [Fact]
    public void DeepRecursionThroughHostFunctionsThrowsARangeError()
    {
        // Without a recursion limit, the .NET stack is the limit: the recursion through host functions and conversions
        // throws a RangeError instead of overflowing the stack of the thread
        using var engine = new JsEngine(new JsEngineOptions { RecursionLimit = int.MaxValue });
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        realm.GlobalObject.SetProperty("convert", realm.CreateFunction("convert", 1, (_, arguments) => engine.ToString(arguments[0])));
        Assert.Equal("RangeError", realm.Execute("""
            const value = { toString() { return convert(value); } };
            try { convert(value); } catch (e) { e.name }
            """).AsString());
    }

    // Each operation calls recurse() again (through the host function convert) from a place where the error leaves a
    // .NET catch block of the engine: the generators, the steps of the iterators and the closing of the iterators
    [Theory]
    [InlineData("function* g() { yield convert(step); } g().next();")]
    [InlineData("Array.from({ [Symbol.iterator]() { return { next() { return convert(step); } }; } });")]
    [InlineData("const a = [0]; Object.defineProperty(a, 0, { get() { return convert(step); } }); new Set(a);")]
    [InlineData("new Set(Array.prototype.values.call({ length: 1, get 0() { return convert(step); } }));")]
    [InlineData("Array.from({ [Symbol.iterator]() { return { next() { return { get done() { return convert(step); } }; } }; } });")]
    [InlineData("Array.from({ [Symbol.iterator]() { return { next() { return { done: false, get value() { return convert(step); } }; } }; } });")]
    [InlineData("Array.from([1], () => convert(step));")]
    [InlineData("new Map([{ get 0() { return convert(step); } }]);")]
    [InlineData("new WeakMap([{ get 0() { return convert(step); } }]);")]
    [InlineData("Map.groupBy([1], () => convert(step));")]
    [InlineData("class S extends Set { add() { return convert(step); } } new S([1]);")]
    [InlineData("Object.fromEntries([{ get 0() { return convert(step); } }]);")]
    [InlineData("Object.groupBy([1], () => convert(step));")]
    [InlineData("[1].values().take({ valueOf() { return convert(step); } });")]
    [InlineData("[1].values().forEach(() => convert(step));")]
    [InlineData("[1].values().join({ toString() { return convert(step); } });")]
    [InlineData("[{ toString() { return convert(step); } }].values().join();")]
    [InlineData("[1].values().map(() => convert(step)).next();")]
    [InlineData("[1].values().flatMap(() => ({ next() { return convert(step); } })).next();")]
    [InlineData("[1].values().flatMap(() => ({ get [Symbol.iterator]() { return convert(step); } })).next();")]
    [InlineData("const h = [1].values().flatMap(() => ({ next() { return { value: 1, done: false }; }, return() { return convert(step); } })); h.next(); h.return();")]
    [InlineData("Iterator.zip([{ next() { return convert(step); } }]).next();")]
    [InlineData("Iterator.zip([{ next() { return { done: true }; } }, { next() { return convert(step); } }], { mode: 'strict' }).next();")]
    [InlineData("Iterator.zip({ [Symbol.iterator]() { return { next() { return convert(step); } }; } });")]
    [InlineData("Iterator.zip([{ get [Symbol.iterator]() { return convert(step); } }]);")]
    [InlineData("Iterator.zip([], { mode: 'longest', padding: { get [Symbol.iterator]() { return convert(step); } } });")]
    [InlineData("Iterator.zip([[]], { mode: 'longest', padding: { [Symbol.iterator]() { return { next() { return convert(step); } }; } } });")]
    [InlineData("Iterator.zip([[]], { mode: 'longest', padding: { [Symbol.iterator]() { return { next() { return { value: 1, done: false }; }, return() { return convert(step); } }; } } });")]
    [InlineData("Iterator.zipKeyed({ get a() { return convert(step); } });")]
    [InlineData("Iterator.zipKeyed({ a: [] }, { mode: 'longest', padding: { get a() { return convert(step); } } });")]
    public void DeepRecursionThroughBuiltinsThrowsARangeError(string operation)
    {
        using var engine = new JsEngine(new JsEngineOptions { RecursionLimit = int.MaxValue });
        var realm = engine.CreateRealm();
        using var scope = engine.Enter();
        realm.GlobalObject.SetProperty("convert", realm.CreateFunction("convert", 1, (_, arguments) => engine.ToString(arguments[0])));
        Assert.Equal("RangeError", realm.Execute($$"""
            const step = { toString() { recurse(); return ''; } };
            function recurse() { {{operation}} }
            try { recurse(); } catch (e) { e.name }
            """).AsString());
    }

    [Fact]
    public void SyntaxErrorsHaveTheirLocation()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var exception = Assert.Throws<JsException>(() => realm.Execute("var a = 1;\nvar = 2;", "script.js"));
        Assert.Equal("script.js", exception.Location?.Url);
        Assert.Equal(2, exception.Location?.Line);

        // The module of an inline script is located in its document
        exception = Assert.Throws<JsException>(() => realm.ParseModule("export const a = 1;\nexport const = 2;", "page.html", hostDefined: null, lineOffset: 10, columnOffset: 4));
        Assert.Equal(new JsSourceLocation("page.html", 12, 14), exception.Location);

        using var scope = engine.Enter();
        var error = realm.CreateError(JsErrorType.TypeError, "custom");
        exception = new JsException(error, new JsSourceLocation("other.js", 3, 4));
        Assert.Equal(new JsSourceLocation("other.js", 3, 4), exception.Location);
        Assert.Equal(error, exception.Value);
    }

    [Fact]
    public void ModuleEvaluationErrorHasItsLocation()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var module = realm.ParseModule("export const a = 1;\n  throw new Error('boom');", "module.js");
        module.LoadRequestedModules();
        module.Link();
        var promise = module.Evaluate().AsObject();
        Assert.True(promise.TryGetPromiseState(out var state, out _, out _));
        Assert.Equal(JsPromiseState.Rejected, state);
        Assert.True(module.HasEvaluationError);
        Assert.Equal(new JsSourceLocation("module.js", 2, 3), module.EvaluationErrorLocation);
    }

    [Fact]
    public void ReadmeExample_Execute()
    {
        #region JavaScriptEngine_Execute
        using var engine = new JsEngine(new JsEngineOptions
        {
            Timeout = TimeSpan.FromSeconds(5), // per entry into JavaScript
            RecursionLimit = 1_000,
        });

        var realm = engine.CreateRealm();

        // Expose a .NET function to the scripts
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", 2, (thisValue, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
        }

        var result = realm.Execute("const values = [1, 2, 3].map(x => add(x, 10)); values.join('-')");
        Assert.Equal("11-12-13", result.AsString());

        // The promise jobs run when the host asks for them
        realm.Execute("globalThis.log = []; Promise.resolve().then(() => log.push('job')); log.push('script');");
        engine.RunJobs();
        Assert.Equal("script,job", realm.Execute("log.join()").AsString());

        // An uncaught JavaScript error is a JsException
        var exception = Assert.Throws<JsException>(() => realm.Execute("null.x"));
        Assert.StartsWith("TypeError", exception.Message, StringComparison.Ordinal);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Modules()
    {
        #region JavaScriptEngine_Modules
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main.js"] = "import { square } from './math.js'; globalThis.result = square(7);",
            ["math.js"] = "export function square(x) { return x * x; }",
        };

        using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new DictionaryModuleLoader(files) });
        var realm = engine.CreateRealm();
        var module = realm.ParseModule(files["main.js"], "main.js");
        module.LoadRequestedModules();
        engine.RunJobs();
        module.Link();
        module.Evaluate();
        engine.RunJobs();

        Assert.Equal(49, realm.Execute("result").AsNumber());
        #endregion
    }

    // The examples of the documentation site (docs/11-JavaScriptEngine)
    [Fact]
    public void Documentation_HelloWorld()
    {
        #region JavaScriptEngine_HelloWorld
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        var result = realm.Execute("const name = 'world'; `Hello, ${name}!`");
        Assert.Equal("Hello, world!", result.AsString());
        #endregion
    }

    [Fact]
    public void Documentation_Realms()
    {
        #region JavaScriptEngine_Realms
        using var engine = new JsEngine();
        var first = engine.CreateRealm();
        var second = engine.CreateRealm();

        // Each realm has its own global object and its own built-ins
        first.Execute("globalThis.owner = 'first'; Array.prototype.last = function () { return this.at(-1); };");
        Assert.Equal("undefined", second.Execute("typeof owner").AsString());
        Assert.Equal("undefined", second.Execute("typeof [].last").AsString());

        // The objects of a realm can be given to the other realms of the same engine
        using (engine.Enter())
        {
            second.GlobalObject.SetProperty("firstGlobal", first.GlobalObject);
        }

        Assert.Equal("first", second.Execute("firstGlobal.owner").AsString());
        Assert.False(second.Execute("firstGlobal.Array === Array").AsBoolean());
        #endregion
    }

    [Fact]
    public void Documentation_EnterScope()
    {
        #region JavaScriptEngine_EnterScope
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        // Execute, Call and Construct enter the engine themselves
        var point = realm.Execute("({ x: 1, y: 2 })").AsObject();

        // The members of the objects are used inside a scope of the engine
        using (engine.Enter())
        {
            point.SetProperty("z", 3);
            Assert.Equal(2, point.GetProperty("y").AsNumber());
            realm.GlobalObject.SetProperty("point", point);
        }

        Assert.Equal("1,2,3", realm.Execute("Object.values(point).join()").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_CompletionValue()
    {
        #region JavaScriptEngine_CompletionValue
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        // The result of a script is the value of its last statement
        var total = realm.Execute("let total = 0; for (const price of [10, 20, 12.5]) { total += price; } total", "prices.js");
        Assert.Equal(42.5, total.AsNumber());

        // The scripts of a realm share their global variables and functions
        realm.Execute("var counter = 1; function increment() { return ++counter; }");
        realm.Execute("increment();");
        Assert.Equal(2, realm.Execute("counter").AsNumber());
        #endregion
    }

    [Fact]
    public void Documentation_PreparedScript()
    {
        #region JavaScriptEngine_PreparedScript
        // Parse and compile once, run many times, in any engine and on any thread
        var script = JsPreparedScript.Parse("function fib(n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); } fib(input)", "fib.js");

        var results = new double[4];
        Parallel.For(0, results.Length, i =>
        {
            // An engine runs on one thread at a time: each thread uses its own engine
            using var engine = new JsEngine();
            var realm = engine.CreateRealm();
            using (engine.Enter())
            {
                realm.GlobalObject.SetProperty("input", 10 + i);
            }

            results[i] = realm.Execute(script).AsNumber();
        });

        Assert.Equal([55, 89, 144, 233], results);
        #endregion
    }

    [Fact]
    public void Documentation_ValidateScript()
    {
        #region JavaScriptEngine_ValidateScript
        // TryParse reports a syntax error without throwing
        if (JsPreparedScript.TryParse("let total = 1;\nlet = ;", "user.js", lineOffset: 0, columnOffset: 0, out var script, out var error))
        {
            Assert.Fail("The script is not valid: " + script.Url);
        }

        Assert.Equal("user.js", error.Url);
        Assert.Equal(2, error.Line);
        Assert.Equal(7, error.Column);
        #endregion
    }

    [Fact]
    public void Documentation_Modules()
    {
        #region JavaScriptEngine_LoadModules
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main.js"] = """
                import { log } from 'app:log';
                import { formatPrice } from './format.js';
                import config from './config.json' with { type: 'json' };

                log(`${import.meta.url}: ${formatPrice(config.price, config.currency)}`);
                export const ready = true;
                """,
            ["./format.js"] = "export function formatPrice(amount, currency) { return `${amount.toFixed(2)} ${currency}`; }",
            ["./config.json"] = """{ "price": 12.5, "currency": "EUR" }""",
        };

        var logs = new List<string>();
        using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new AppModuleLoader(files, logs) });
        var realm = engine.CreateRealm();

        // Load the module and its dependencies, link them, then evaluate them
        var module = realm.ParseModule(files["main.js"], "main.js");
        module.LoadRequestedModules();
        engine.RunJobs();
        module.Link();
        var evaluation = module.Evaluate().AsObject();
        engine.RunJobs();

        Assert.True(evaluation.TryGetPromiseState(out var state, out _, out _));
        Assert.Equal(JsPromiseState.Fulfilled, state);
        Assert.Equal(["main.js: 12.50 EUR"], logs);

        // The exports of the module
        using (engine.Enter())
        {
            var exports = module.GetNamespace().AsObject();
            Assert.True(exports.GetProperty("ready").AsBoolean());
        }
        #endregion
    }

    [Fact]
    public void Documentation_ModuleErrors()
    {
        #region JavaScriptEngine_ModuleErrors
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["main.js"] = "import { missing } from './missing.js';",
        };

        using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new AppModuleLoader(files, []) });
        var realm = engine.CreateRealm();
        var module = realm.ParseModule(files["main.js"], "main.js");

        // A module that cannot be loaded rejects the promise of LoadRequestedModules
        var loading = module.LoadRequestedModules().AsObject();
        engine.RunJobs();
        Assert.True(loading.TryGetPromiseState(out var state, out var reason, out _));
        Assert.Equal(JsPromiseState.Rejected, state);
        Assert.Equal("TypeError: Cannot find the module './missing.js'", engine.ToString(reason));
        #endregion
    }

    [Fact]
    public void Documentation_DynamicImport()
    {
        #region JavaScriptEngine_DynamicImport
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["./plugin.js"] = "export default function run(input) { return input.toUpperCase(); }",
        };

        using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new AppModuleLoader(files, []) });
        var realm = engine.CreateRealm();

        // A script can import a module with import(): the loading runs from the job queue
        realm.Execute("import('./plugin.js').then(plugin => { globalThis.result = plugin.default('done'); });", "app.js");
        engine.RunJobs();

        Assert.Equal("DONE", realm.Execute("result").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_CatchErrors()
    {
        #region JavaScriptEngine_CatchErrors
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        var exception = Assert.Throws<JsException>(() => realm.Execute("function check(order) {\n  if (!order.id) throw new RangeError('The order has no id');\n}\ncheck({});", "orders.js"));

        // The thrown value, and where it was thrown
        Assert.Equal("RangeError: The order has no id", exception.Message);
        Assert.Equal(new JsSourceLocation("orders.js", 2, 18), exception.Location);
        using (engine.Enter())
        {
            var error = exception.Value.AsObject();
            Assert.Equal("RangeError", error.GetProperty("name").AsString());
            Assert.Equal("The order has no id", error.GetProperty("message").AsString());
            Assert.Equal("RangeError: The order has no id\n    at check (orders.js:2:28)\n    at orders.js:4:1", error.GetErrorStack());
        }

        // A script can throw any value
        exception = Assert.Throws<JsException>(() => realm.Execute("throw 42;"));
        Assert.Equal(42, exception.Value.AsNumber());
        #endregion
    }

    [Fact]
    public void Documentation_ThrowFromDotNet()
    {
        #region JavaScriptEngine_ThrowFromDotNet
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("parseAge", realm.CreateFunction("parseAge", 1, (_, arguments) =>
            {
                var text = arguments.Length > 0 ? engine.ToString(arguments[0]) : "";
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var age))
                {
                    // A JavaScript error, which the scripts can catch
                    throw realm.CreateException(JsErrorType.TypeError, "Invalid age: " + text);
                }

                return age;
            }));
        }

        Assert.Equal(42, realm.Execute("parseAge('42')").AsNumber());
        Assert.Equal("TypeError: Invalid age: abc", realm.Execute("try { parseAge('abc'); } catch (e) { `${e}` }").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_DotNetExceptionsAreNotCatchable()
    {
        #region JavaScriptEngine_DotNetExceptions
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("fail", realm.CreateFunction("fail", 0, (_, _) => throw new InvalidOperationException("Unexpected state")));
        }

        // A .NET exception is not a JavaScript error: the script cannot catch it, and it goes to the caller
        Assert.Throws<InvalidOperationException>(() => realm.Execute("try { fail(); } catch { }"));
        #endregion
    }

    [Fact]
    public void Documentation_Values()
    {
        #region JavaScriptEngine_Values
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        static string Describe(JsValue value) => value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "null",
            JsValueKind.Boolean => value.AsBoolean() ? "true" : "false",
            JsValueKind.Number => value.AsNumber().ToString(CultureInfo.InvariantCulture),
            JsValueKind.String => '"' + value.AsString() + '"',
            JsValueKind.BigInt => value.AsBigInt().ToString(CultureInfo.InvariantCulture) + "n",
            JsValueKind.Symbol => value.AsSymbol().ToString(),
            JsValueKind.Object when value.IsCallable => "function",
            JsValueKind.Object => "object",
            _ => throw new UnreachableException(),
        };

        var values = (JsArray)realm.Execute("[42, 'text', true, null, undefined, 2n ** 70n, Symbol('id'), {}, Math.max]").AsObject();
        using (engine.Enter())
        {
            var descriptions = new List<string>();
            for (var i = 0u; i < values.Length; i++)
            {
                descriptions.Add(Describe(values.GetProperty(i.ToString(CultureInfo.InvariantCulture))));
            }

            Assert.Equal(["42", "\"text\"", "true", "null", "undefined", "1180591620717411303424n", "Symbol(id)", "object", "function"], descriptions);
        }
        #endregion
    }

    [Fact]
    public void Documentation_CreateValues()
    {
        #region JavaScriptEngine_CreateValues
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            // The numbers, the strings and the booleans convert implicitly to JsValue
            realm.GlobalObject.SetProperty("count", 3);
            realm.GlobalObject.SetProperty("ratio", 0.5);
            realm.GlobalObject.SetProperty("title", "Report");
            realm.GlobalObject.SetProperty("enabled", true);
            realm.GlobalObject.SetProperty("nothing", JsValue.Null);
            realm.GlobalObject.SetProperty("big", JsValue.FromBigInt(BigInteger.Pow(2, 70)));
        }

        Assert.Equal("number,number,string,boolean,object,bigint", realm.Execute("[count, ratio, title, enabled, nothing, big].map(x => typeof x).join()").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_Conversions()
    {
        #region JavaScriptEngine_Conversions
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        // AsNumber, AsString... read a value of their kind: check the kind first
        var length = realm.Execute("[1, 2, 3].length");
        Assert.True(length.IsNumber);
        Assert.Equal(3, length.AsNumber());

        // The conversions of JavaScript accept any value, and may run code of the script (valueOf, toString)
        var price = realm.Execute("({ amount: 5, valueOf() { return this.amount * 2; } })");
        Assert.Equal(10, engine.ToNumber(price));
        Assert.Equal("[object Object]", engine.ToString(price));
        Assert.True(JsEngine.ToBoolean(realm.Execute("'0'")));
        Assert.Equal(12, engine.ToNumber(realm.Execute("'12'")));
        #endregion
    }

    [Fact]
    public void Documentation_Dates()
    {
        #region JavaScriptEngine_Dates
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            // A Date is a number of milliseconds since 1970-01-01T00:00:00Z
            var date = realm.CreateDate(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
            realm.GlobalObject.SetProperty("date", date);
        }

        Assert.Equal("2026-10-29T12:00:00.000Z", realm.Execute("date.toISOString()").AsString());

        var returned = realm.Execute("new Date(Date.UTC(2000, 0, 1))").AsObject();
        Assert.True(returned.TryGetDateValue(out var timeValue));
        Assert.Equal(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.FromUnixTimeMilliseconds((long)timeValue));
        #endregion
    }

    [Fact]
    public void Documentation_Json()
    {
        #region JavaScriptEngine_Json
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute("""
            function summarize(order) {
              return { id: order.id, count: order.items.length, total: order.items.reduce((sum, item) => sum + item.price, 0) };
            }
            """);

        using (engine.Enter())
        {
            var json = realm.GlobalObject.GetProperty("JSON").AsObject();
            var parse = json.GetProperty("parse");
            var stringify = json.GetProperty("stringify");

            // .NET to JavaScript: parse a JSON text, e.g. the output of System.Text.Json
            var order = engine.Call(parse, json, """{ "id": "A-12", "items": [{ "price": 5 }, { "price": 7.5 }] }""");

            var summary = engine.Call(realm.GlobalObject.GetProperty("summarize"), JsValue.Undefined, order);

            // JavaScript to .NET: a JSON text
            Assert.Equal("""{"id":"A-12","count":2,"total":12.5}""", engine.Call(stringify, json, summary).AsString());
        }
        #endregion
    }

    [Fact]
    public void Documentation_CreateObjects()
    {
        #region JavaScriptEngine_CreateObjects
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            var settings = realm.CreateObject();
            settings.SetProperty("theme", "dark");
            settings.SetProperty("fontSize", 14);
            settings.SetProperty("languages", realm.CreateArray(["en", "fr"]));

            // A read-only property, which the scripts do not enumerate
            settings.DefineProperty("version", JsPropertyDescriptor.Data("1.0", writable: false, enumerable: false, configurable: false));

            realm.GlobalObject.SetProperty("settings", settings);
        }

        Assert.Equal("theme,fontSize,languages", realm.Execute("Object.keys(settings).join()").AsString());
        Assert.Equal("TypeError", realm.Execute("'use strict'; try { settings.version = '2.0'; } catch (e) { e.name }").AsString());
        Assert.Equal("1.0", realm.Execute("settings.version").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_ReadObjects()
    {
        #region JavaScriptEngine_ReadObjects
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var user = realm.Execute("({ name: 'Ada', roles: ['admin', 'dev'], [Symbol.for('id')]: 1 })").AsObject();

        using (engine.Enter())
        {
            Assert.Equal("Ada", user.GetProperty("name").AsString());

            // An array is a JsArray: its elements are the properties "0", "1"...
            var roles = (JsArray)user.GetProperty("roles").AsObject();
            Assert.Equal(2u, roles.Length);
            Assert.Equal("dev", roles.GetProperty("1").AsString());

            // The keys are strings or symbols
            Assert.Equal(["name", "roles"], user.GetOwnPropertyKeys().Where(key => key.IsString).Select(key => key.AsString()));

            // A missing property is undefined
            Assert.True(user.GetProperty("email").IsUndefined);
            Assert.False(user.ContainsProperty("email"));
        }
        #endregion
    }

    [Fact]
    public void Documentation_LazyProperties()
    {
        #region JavaScriptEngine_LazyProperties
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var created = 0;
        using (engine.Enter())
        {
            // The value is created the first time a script reads the property
            realm.GlobalObject.DefineLazyDataProperty("catalog", () =>
            {
                created++;
                return realm.CreateArray(["apple", "pear"]);
            }, writable: false, enumerable: false, configurable: true);
        }

        realm.Execute("1 + 1");
        Assert.Equal(0, created);
        Assert.Equal("apple,pear", realm.Execute("catalog.join()").AsString());
        Assert.Equal(1, created);
        #endregion
    }

    [Fact]
    public void Documentation_Functions()
    {
        #region JavaScriptEngine_Functions
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            // The name and the length are the name and length properties of the function
            realm.GlobalObject.SetProperty("formatPrice", realm.CreateFunction("formatPrice", 2, (thisValue, arguments) =>
            {
                // The arguments are the ones of the call: a script can pass fewer than the length of the function
                if (arguments.Length < 1 || !arguments[0].IsNumber)
                {
                    throw realm.CreateException(JsErrorType.TypeError, "The amount must be a number");
                }

                var currency = arguments.Length > 1 ? engine.ToString(arguments[1]) : "EUR";
                return arguments[0].AsNumber().ToString("F2", CultureInfo.InvariantCulture) + " " + currency;
            }));
        }

        Assert.Equal("12.50 EUR", realm.Execute("formatPrice(12.5)").AsString());
        Assert.Equal("3.00 USD", realm.Execute("formatPrice(3, 'USD')").AsString());
        Assert.Equal("formatPrice,2", realm.Execute("[formatPrice.name, formatPrice.length].join()").AsString());
        Assert.Equal("TypeError: The amount must be a number", realm.Execute("try { formatPrice('x'); } catch (e) { `${e}` }").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_CallJavaScript()
    {
        #region JavaScriptEngine_CallJavaScript
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute("""
            function applyDiscount(order, percent) {
              return { ...order, total: order.total * (1 - percent / 100) };
            }
            """);

        using (engine.Enter())
        {
            var order = realm.CreateObject();
            order.SetProperty("id", "A-12");
            order.SetProperty("total", 80);

            // Call(function, this, arguments...)
            var applyDiscount = realm.GlobalObject.GetProperty("applyDiscount");
            var discounted = engine.Call(applyDiscount, JsValue.Undefined, order, 25).AsObject();
            Assert.Equal("A-12", discounted.GetProperty("id").AsString());
            Assert.Equal(60, discounted.GetProperty("total").AsNumber());

            // Construct(constructor, arguments): new Map([["a", 1]])
            var map = engine.Construct(realm.GlobalObject.GetProperty("Map"), [realm.CreateArray([realm.CreateArray(["a", 1])])]).AsObject();
            Assert.Equal(1, map.GetProperty("size").AsNumber());
        }
        #endregion
    }

    [Fact]
    public void Documentation_ExposeDotNetClass()
    {
        #region JavaScriptEngine_ExposeClass
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            // The methods are defined once, on the prototype that the instances share
            var prototype = realm.CreateObject();
            prototype.SetProperty("append", realm.CreateFunction("append", 1, (thisValue, arguments) =>
            {
                GetBuilder(thisValue).Append(engine.ToString(arguments.Length > 0 ? arguments[0] : JsValue.Undefined));
                return thisValue;
            }));
            prototype.SetProperty("toString", realm.CreateFunction("toString", 0, (thisValue, _) => GetBuilder(thisValue).ToString()));

            var constructor = realm.CreateConstructor("TextBuilder", 0, (_, _, newTarget) =>
            {
                if (newTarget is null)
                {
                    throw realm.CreateException(JsErrorType.TypeError, "Constructor TextBuilder requires 'new'");
                }

                // The prototype of newTarget lets the scripts extend the class
                var instancePrototype = newTarget.GetProperty("prototype").TryGetObject() ?? prototype;
                return new JsHostObject(instancePrototype, new StringBuilder());
            });
            constructor.DefineProperty("prototype", JsPropertyDescriptor.Data(prototype, writable: false, enumerable: false, configurable: false));
            prototype.DefineProperty("constructor", JsPropertyDescriptor.Data(constructor, writable: true, enumerable: false, configurable: true));
            realm.GlobalObject.SetProperty("TextBuilder", constructor);

            // The .NET object of an instance is the target of its host object
            StringBuilder GetBuilder(JsValue thisValue) => thisValue.TryGetObject() is JsHostObject { Target: StringBuilder builder }
                ? builder
                : throw realm.CreateException(JsErrorType.TypeError, "Illegal invocation");
        }

        Assert.Equal("Hello, 42", realm.Execute("const builder = new TextBuilder(); builder.append('Hello').append(', ').append(42); `${builder}`").AsString());
        Assert.Equal("true,[x]", realm.Execute("class Brackets extends TextBuilder { wrap(text) { return this.append(`[${text}]`); } } const b = new Brackets(); [b instanceof TextBuilder, `${b.wrap('x')}`].join()").AsString());
        Assert.Equal("TypeError", realm.Execute("try { TextBuilder.prototype.append.call({}, 'x'); } catch (e) { e.name }").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_HostObject()
    {
        #region JavaScriptEngine_HostObject
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["REGION"] = "eu-west", ["STAGE"] = "test" };
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("env", new ReadOnlyDictionaryObject(realm, environment));
        }

        Assert.Equal("eu-west", realm.Execute("env.REGION").AsString());

        // The object reads the dictionary on each access
        environment["STAGE"] = "production";
        Assert.Equal("""{"REGION":"eu-west","STAGE":"production"}""", realm.Execute("JSON.stringify(env)").AsString());
        Assert.Equal("TypeError", realm.Execute("'use strict'; try { env.REGION = 'us'; } catch (e) { e.name }").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_Buffers()
    {
        #region JavaScriptEngine_Buffers
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var pixels = new byte[] { 10, 20, 30, 40 };
        var detachKey = "pixels";
        JsObject buffer;
        using (engine.Enter())
        {
            // The ArrayBuffer uses the memory of the array: nothing is copied. With a detach key, the scripts cannot
            // transfer or detach it
            buffer = realm.CreateArrayBuffer(pixels.AsMemory(), detachKey);
            realm.GlobalObject.SetProperty("pixels", realm.CreateBufferView(JsBufferKind.Uint8Array, buffer, byteOffset: 0, length: pixels.Length));
        }

        realm.Execute("for (let i = 0; i < pixels.length; i++) pixels[i] = 255 - pixels[i];");
        Assert.Equal([245, 235, 225, 215], pixels);

        // Before the memory is reused, detach the buffer: the scripts then see an empty buffer
        JsBinaryData.DetachArrayBuffer(buffer, detachKey);
        Assert.Equal(0, realm.Execute("pixels.length").AsNumber());

        // Read the bytes of a buffer or a view created by a script
        var result = realm.Execute("Uint8Array.from([1, 2, 3])");
        Assert.True(JsBinaryData.TryGetBufferSource(result, out var kind, out var isShared, out var bytes));
        Assert.Equal(JsBufferKind.Uint8Array, kind);
        Assert.False(isShared);
        Assert.Equal([1, 2, 3], bytes.ToArray());
        #endregion
    }

    [Fact]
    public void Documentation_AsyncFunctions()
    {
        #region JavaScriptEngine_AsyncFunctions
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var promise = realm.Execute("""
            async function compute() {
              const values = await Promise.all([1, 2, 3].map(async x => x * 10));
              return values.reduce((a, b) => a + b);
            }
            compute()
            """).AsObject();

        // The script ran until its first await: the rest runs from the job queue
        Assert.True(promise.TryGetPromiseState(out var state, out _, out _));
        Assert.Equal(JsPromiseState.Pending, state);

        engine.RunJobs();
        Assert.True(promise.TryGetPromiseState(out state, out var result, out _));
        Assert.Equal(JsPromiseState.Fulfilled, state);
        Assert.Equal(60, result.AsNumber());
        #endregion
    }

    [Fact]
    public async Task Documentation_AwaitDotNetTasks()
    {
        #region JavaScriptEngine_AwaitDotNetTasks
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var pending = new Queue<(Task<string> Task, JsValue Resolve, JsValue Reject)>();
        using (engine.Enter())
        {
            var promiseConstructor = realm.GlobalObject.GetProperty("Promise");
            realm.GlobalObject.SetProperty("readText", realm.CreateFunction("readText", 1, (_, arguments) =>
            {
                var task = ReadTextAsync(arguments.Length > 0 ? engine.ToString(arguments[0]) : "");

                // new Promise((resolve, reject) => ...): keep the resolving functions until the task completes
                var executor = realm.CreateFunction("executor", 2, (_, resolvingFunctions) =>
                {
                    pending.Enqueue((task, resolvingFunctions[0], resolvingFunctions[1]));
                    return JsValue.Undefined;
                });
                return engine.Construct(promiseConstructor, [executor]);
            }));
        }

        var promise = realm.Execute("""
            (async () => {
              const text = await readText('a.txt');
              try {
                await readText('missing.txt');
              } catch (e) {
                return `${text} | ${e.message}`;
              }
            })()
            """).AsObject();

        // The event loop of the host: wait for the .NET work, settle its promise, then run the jobs it unblocks
        while (pending.TryDequeue(out var item))
        {
            try
            {
                var text = await item.Task;
                engine.Call(item.Resolve, JsValue.Undefined, text);
            }
            catch (FileNotFoundException exception)
            {
                using (engine.Enter())
                {
                    engine.Call(item.Reject, JsValue.Undefined, realm.CreateError(JsErrorType.Error, exception.Message));
                }
            }

            engine.RunJobs();
        }

        Assert.True(promise.TryGetPromiseState(out var state, out var result, out _));
        Assert.Equal(JsPromiseState.Fulfilled, state);
        Assert.Equal("content of a.txt | missing.txt not found", result.AsString());

        static async Task<string> ReadTextAsync(string name)
        {
            await Task.Yield();
            return name is "missing.txt" ? throw new FileNotFoundException(name + " not found") : "content of " + name;
        }
        #endregion
    }

    [Fact]
    public void Documentation_UnhandledRejections()
    {
        #region JavaScriptEngine_UnhandledRejections
        var host = new RejectionTrackingHost();
        using var engine = new JsEngine(new JsEngineOptions { Host = host });
        var realm = engine.CreateRealm();
        realm.Execute("""
            Promise.reject(new Error('handled')).catch(() => {});
            Promise.reject(new Error('forgotten'));
            """);
        engine.RunJobs();

        // Once the jobs have run, report the promises that are still rejected without a handler
        var promise = Assert.Single(host.UnhandledRejections);
        Assert.True(promise.TryGetPromiseState(out _, out var reason, out _));
        Assert.Equal("Error: forgotten", engine.ToString(reason));
        #endregion
    }

    [Fact]
    public void Documentation_Limits()
    {
        #region JavaScriptEngine_Limits
        using var engine = new JsEngine(new JsEngineOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            MaxStatements = 1_000_000,
            MemoryLimit = 64 * 1024 * 1024,
            RecursionLimit = 500,
            RegExpTimeout = TimeSpan.FromMilliseconds(100),
        });
        var realm = engine.CreateRealm();

        // A limit stops the script: the script cannot catch the termination, and its finally blocks do not run
        var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("try { for (;;) {} } finally { globalThis.cleanedUp = true; }"));
        Assert.Equal(JsTerminationReason.StatementLimit, exception.Reason);
        Assert.Equal("undefined", realm.Execute("typeof cleanedUp").AsString());

        // Each entry into JavaScript gets the full limits again
        Assert.Equal(4, realm.Execute("2 + 2").AsNumber());

        // A too deep recursion is a RangeError, which the scripts can catch
        Assert.Equal("RangeError", realm.Execute("function f() { return 1 + f(); } try { f(); } catch (e) { e.name }").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_Cancellation()
    {
        #region JavaScriptEngine_Cancellation
        using var cancellation = new CancellationTokenSource();
        using var engine = new JsEngine(new JsEngineOptions { CancellationToken = cancellation.Token });
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("processItem", realm.CreateFunction("processItem", 1, (_, arguments) =>
            {
                if (engine.ToNumber(arguments[0]) >= 3)
                {
                    cancellation.Cancel(); // e.g. the user clicked Cancel, from another thread
                }

                // A .NET function that works for a long time checks the limits and the cancellation itself
                engine.CheckInterrupt();
                return JsValue.Undefined;
            }));
        }

        var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("for (let i = 0; ; i++) processItem(i);"));
        Assert.Equal(JsTerminationReason.Canceled, exception.Reason);
        #endregion
    }

    [Fact]
    public void Documentation_NoStringCompilation()
    {
        #region JavaScriptEngine_NoStringCompilation
        using var engine = new JsEngine(new JsEngineOptions { Host = new NoStringCompilationHost() });
        var realm = engine.CreateRealm();

        Assert.Equal("EvalError,EvalError", realm.Execute("""
            const errors = [];
            try { eval('1 + 1'); } catch (e) { errors.push(e.name); }
            try { new Function('return 1'); } catch (e) { errors.push(e.name); }
            errors.join()
            """).AsString());
        #endregion
    }

    [Fact]
    public void Documentation_Locale()
    {
        #region JavaScriptEngine_Locale
        using var engine = new JsEngine(new JsEngineOptions { DefaultLocale = "de-DE", TimeZone = "Europe/Berlin" });
        var realm = engine.CreateRealm();

        // The default locale and time zone of the scripts
        Assert.Equal("1.234,5", realm.Execute("(1234.5).toLocaleString()").AsString());
        Assert.Equal("29.10.2026, 13:00:00", realm.Execute("new Date(Date.UTC(2026, 9, 29, 12)).toLocaleString()").AsString());
        Assert.Equal("de-DE|Europe/Berlin", realm.Execute("const options = new Intl.DateTimeFormat().resolvedOptions(); `${options.locale}|${options.timeZone}`").AsString());

        // The scripts can ask for another locale or time zone
        Assert.Equal("1,234.5", realm.Execute("new Intl.NumberFormat('en-US').format(1234.5)").AsString());
        Assert.Equal("29 octobre 2026", realm.Execute("new Date(Date.UTC(2026, 9, 29, 12)).toLocaleDateString('fr-FR', { dateStyle: 'long', timeZone: 'UTC' })").AsString());
        #endregion
    }

    [Fact]
    public void Documentation_Clock()
    {
        #region JavaScriptEngine_Clock
        using var engine = new JsEngine(new JsEngineOptions
        {
            TimeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 10, 29, 23, 30, 0, TimeSpan.Zero)),
            TimeZone = "Asia/Tokyo",
        });
        var realm = engine.CreateRealm();

        Assert.Equal("2026-10-29T23:30:00.000Z", realm.Execute("new Date().toISOString()").AsString());
        Assert.Equal("2026-10-30", realm.Execute("Temporal.Now.plainDateISO().toString()").AsString());
        Assert.Equal(8, realm.Execute("new Date().getHours()").AsNumber());
        #endregion
    }

    /// <summary>Loads the modules from a dictionary: the specifiers are the keys (e.g. <c>./math.js</c> is <c>math.js</c>).</summary>
    private sealed class DictionaryModuleLoader(Dictionary<string, string> files) : JsModuleLoader
    {
        private readonly Dictionary<string, JsModule> _modules = new(StringComparer.Ordinal);

        protected internal override void LoadImportedModule(JsModuleLoadRequest request)
        {
            var name = request.Specifier.StartsWith("./", StringComparison.Ordinal) ? request.Specifier[2..] : request.Specifier;
            if (!files.TryGetValue(name, out var code))
            {
                request.Fail("Cannot find the module '" + request.Specifier + "'");
                return;
            }

            if (!_modules.TryGetValue(name, out var module))
            {
                module = request.Realm.ParseModule(code, name);
                _modules[name] = module;
            }

            request.Complete(module);
        }
    }

    private sealed class UppercaseHostObject(JsObject prototype, object target) : JsHostObject(prototype, target)
    {
        protected override bool GetOwnPropertyCore(JsPropertyKey key, out JsPropertyDescriptor descriptor)
        {
            if (key.IsString && key.AsString() == "name")
            {
                descriptor = JsPropertyDescriptor.Data("NAME", writable: false, enumerable: false, configurable: true);
                return true;
            }

            return base.GetOwnPropertyCore(key, out descriptor);
        }
    }

    private sealed class CallableHostObject(JsObject prototype) : JsHostObject(prototype)
    {
        protected override JsValue CallCore(JsValue thisValue, ReadOnlySpan<JsValue> arguments) => "called " + arguments.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class TrustedCodeHost : JsHost
    {
        public override string? GetCodeForEval(JsEngine engine, JsValue argument)
        {
            if (argument.TryGetObject() is { } obj && obj.TryGetOwnProperty("trustedCode", out var descriptor))
                return descriptor.Value.AsString();

            return base.GetCodeForEval(engine, argument);
        }
    }

    private sealed class RecordingHost : JsHost
    {
        public List<JsPromiseRejectionOperation> Rejections { get; } = [];

        public List<JsRealm?> RejectionRealms { get; } = [];

        public List<string> Compilations { get; } = [];

        public int Callbacks { get; private set; }

        public override void PromiseRejectionTracker(JsEngine engine, JsObject promise, JsPromiseRejectionOperation operation)
        {
            Rejections.Add(operation);
            RejectionRealms.Add(engine.CurrentRealm);
        }

        public override void EnsureCanCompileStrings(JsEngine engine, JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
            => Compilations.Add(compilationType + "|" + bodyString);

        public override JsValue CallJobCallback(JsEngine engine, in JsJobCallback jobCallback, JsValue thisValue, ReadOnlySpan<JsValue> arguments)
        {
            Callbacks++;
            return base.CallJobCallback(engine, jobCallback, thisValue, arguments);
        }
    }
}
