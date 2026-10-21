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
        Assert.Equal(2, location?.Line);
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

    private sealed class RecordingHost : JsHost
    {
        public List<JsPromiseRejectionOperation> Rejections { get; } = [];

        public List<string> Compilations { get; } = [];

        public int Callbacks { get; private set; }

        public override void PromiseRejectionTracker(JsEngine engine, JsObject promise, JsPromiseRejectionOperation operation) => Rejections.Add(operation);

        public override void EnsureCanCompileStrings(JsEngine engine, JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
            => Compilations.Add(compilationType + "|" + bodyString);

        public override JsValue CallJobCallback(JsEngine engine, in JsJobCallback jobCallback, JsValue thisValue, ReadOnlySpan<JsValue> arguments)
        {
            Callbacks++;
            return base.CallJobCallback(engine, jobCallback, thisValue, arguments);
        }
    }
}
