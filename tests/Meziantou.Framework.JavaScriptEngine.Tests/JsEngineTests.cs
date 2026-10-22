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
