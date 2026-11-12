using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Tests;

// The contract of the embedding API: values, scopes, threads, ownership and options.
public sealed class JsEngineEmbeddingTests
{
    [Fact]
    public void JsException_MessageConstructorsThrowAnError()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("withMessage", realm.CreateFunction("withMessage", 0, (_, _) => throw new JsException("bad")));
            realm.GlobalObject.SetProperty("withInner", realm.CreateFunction("withInner", 0, (_, _) => throw new JsException("inner", new InvalidOperationException("cause"))));
            realm.GlobalObject.SetProperty("withoutMessage", realm.CreateFunction("withoutMessage", 0, (_, _) => throw new JsException()));
        }

        Assert.Equal("Error|bad|true", realm.Execute("try { withMessage(); } catch (e) { [e.name, e.message, e instanceof Error].join('|') }").AsString());
        Assert.Equal("Error|inner", realm.Execute("try { withInner(); } catch (e) { [e.name, e.message].join('|') }").AsString());
        Assert.Equal("Error|", realm.Execute("try { withoutMessage(); } catch (e) { [e.name, e.message].join('|') }").AsString());

        // Not caught by the script: the exception keeps its message, and its value is the error
        var exception = Assert.Throws<JsException>(() => realm.Execute("withMessage()"));
        Assert.Equal("bad", exception.Message);
        using (engine.Enter())
        {
            Assert.Equal("bad", exception.Value.AsObject().GetProperty("message").AsString());
        }
    }

    [Fact]
    public void JsException_ValueIsUndefinedWithoutEngine()
    {
        var exception = new JsException("bad");
        Assert.True(exception.Value.IsUndefined);
    }

    [Fact]
    public void JsValue_AccessorsCheckTheKind()
    {
        var text = JsValue.FromString("5");
        var exception = Assert.Throws<InvalidOperationException>(() => text.AsNumber());
        Assert.Equal("The value is not a number: it is a string", exception.Message);
        Assert.Throws<InvalidOperationException>(() => JsValue.Undefined.AsObject());
        Assert.Throws<InvalidOperationException>(() => JsValue.Null.AsString());
        Assert.Throws<InvalidOperationException>(() => new JsValue(1).AsBoolean());
        Assert.Throws<InvalidOperationException>(() => JsValue.True.AsBigInt());
        Assert.Throws<InvalidOperationException>(() => JsValue.True.AsSymbol());

        Assert.False(text.TryGetNumber(out _));
        Assert.True(new JsValue(2.5).TryGetNumber(out var number));
        Assert.Equal(2.5, number);
        Assert.True(text.TryGetString(out var str));
        Assert.Equal("5", str);
        Assert.False(JsValue.Null.TryGetString(out _));
        Assert.True(JsValue.False.TryGetBoolean(out var boolean));
        Assert.False(boolean);
        Assert.False(JsValue.Undefined.TryGetBoolean(out _));
    }

    [Fact]
    public void ObjectMembers_RequireAnEngineScope()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var obj = realm.Execute("({ data: 1, get x() { return [1, 2].map(a => a).join(); }, set y(v) { this.data = [v].map(a => a * 2)[0]; } })").AsObject();
        var proxy = realm.Execute("new Proxy({}, { get: () => [3].map(a => a).join() })").AsObject();

        foreach (var action in new Action[]
        {
            () => obj.GetProperty("data"),
            () => obj.GetProperty("x"),
            () => obj.SetProperty("y", 2),
            () => obj.ContainsProperty("x"),
            () => obj.DeleteProperty("data"),
            () => obj.GetOwnPropertyKeys(),
            () => obj.GetPrototype(),
            () => obj.TryGetOwnProperty("x", out _),
            () => proxy.GetProperty("a"),
        })
        {
            var exception = Assert.Throws<InvalidOperationException>(action);
            Assert.StartsWith("No engine is entered on the current thread", exception.Message);
        }

        using (engine.Enter())
        {
            Assert.Equal("1,2", obj.GetProperty("x").AsString());
            Assert.True(obj.SetProperty("y", 2));
            Assert.Equal(4, obj.GetProperty("data").AsNumber());
            Assert.Equal("3", proxy.GetProperty("a").AsString());
        }
    }

    [Fact]
    public void Engines_RejectTheObjectsOfAnotherEngine()
    {
        using var engine1 = new JsEngine();
        using var engine2 = new JsEngine();
        var realm1 = engine1.CreateRealm();
        var realm2 = engine2.CreateRealm();
        var function2 = realm2.Execute("(function () { return 2; })");
        var object2 = realm2.Execute("({ a: 1 })");
        var constructor2 = realm2.Execute("(class A {})");

        Assert.Throws<InvalidOperationException>(() => engine1.Call(function2, JsValue.Undefined));
        Assert.Throws<InvalidOperationException>(() => engine1.Call(realm1.Execute("(function (x) { return x; })"), JsValue.Undefined, object2));
        Assert.Throws<InvalidOperationException>(() => engine1.Construct(constructor2, []));
        Assert.Throws<InvalidOperationException>(() => engine1.RunInRealm(realm2, () => 1));
        using (engine1.Enter())
        {
            Assert.Throws<InvalidOperationException>(() => realm1.GlobalObject.SetProperty("f", function2));
            Assert.Throws<InvalidOperationException>(() => realm1.GlobalObject.SetProperty("o", object2));
            Assert.Throws<InvalidOperationException>(() => object2.AsObject().GetProperty("a"));
            Assert.Throws<InvalidOperationException>(() => realm1.GlobalObject.DefineProperty("g", JsPropertyDescriptor.Accessor(function2, JsValue.Undefined)));

            // The primitive values can be shared
            Assert.True(realm1.GlobalObject.SetProperty("s", realm2.Execute("'text'")));
        }

        // The realms of the same engine share their objects
        var realm1b = engine1.CreateRealm();
        using (engine1.Enter())
        {
            Assert.True(realm1b.GlobalObject.SetProperty("f", realm1.Execute("(function () { return 'first'; })")));
        }

        Assert.Equal("first", realm1b.Execute("f()").AsString());
        Assert.Equal(2, engine2.Call(function2, JsValue.Undefined).AsNumber());
    }

    [Fact]
    public async Task Engine_RejectsConcurrentAccess()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("block", realm.CreateFunction("block", 0, (_, _) =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                return JsValue.Undefined;
            }));
        }

        var running = Task.Run(() => realm.Execute("block(); 1"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        try
        {
            var exception = await Task.Run(() => Assert.Throws<InvalidOperationException>(() => realm.Execute("2")));
            Assert.StartsWith("The engine is used by another thread", exception.Message);
            await Task.Run(() => Assert.Throws<InvalidOperationException>(() => engine.Enter()));
            await Task.Run(() => Assert.Throws<InvalidOperationException>(() => engine.EnterExecutionScope()));
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(1, (await running).AsNumber());

        // Once the first thread has left the engine, another thread can use it
        Assert.Equal(3, (await Task.Run(() => realm.Execute("3"))).AsNumber());
    }

    [Fact]
    public void Engine_AllowsReentryOnTheOwningThread()
    {
        using var engine1 = new JsEngine();
        using var engine2 = new JsEngine();
        var realm1 = engine1.CreateRealm();
        var realm2 = engine2.CreateRealm();
        using (engine1.Enter())
        {
            // A callback of engine 1 uses engine 2, which calls back into engine 1
            realm1.GlobalObject.SetProperty("viaEngine2", realm1.CreateFunction("viaEngine2", 0, (_, _) =>
            {
                using (engine1.Enter())
                {
                    _ = realm1.Execute("1");
                }

                return realm2.Execute("realm1Again()").AsNumber() + 1;
            }));
        }

        using (engine2.Enter())
        {
            realm2.GlobalObject.SetProperty("realm1Again", realm2.CreateFunction("realm1Again", 0, (_, _) => realm1.Execute("40")));
        }

        Assert.Equal(41, realm1.Execute("viaEngine2()").AsNumber());
    }

    [Fact]
    public async Task Engines_RunConcurrentlyOnTheirOwnThreads()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
        {
            using var engine = new JsEngine();
            var realm = engine.CreateRealm();
            double sum = 0;
            for (var j = 0; j < 50; j++)
            {
                sum += realm.Execute($"let s{j} = 0; for (let k = 0; k < 1000; k++) s{j} += {i}; s{j}").AsNumber();
            }

            return sum;
        })));

        Assert.Equal([0d, 50_000d, 100_000d, 150_000d], results);
    }

    [Fact]
    public void Options_AreCopiedByTheEngine()
    {
        var options = new JsEngineOptions { MaxStatements = 1_000 };
        using var engine1 = new JsEngine(options);
        options.MaxStatements = null;
        using var engine2 = new JsEngine(options);

        // Changing the options after the creation of an engine does not change it
        Assert.NotSame(options, engine1.Options);
        Assert.Equal(1_000, engine1.Options.MaxStatements);
        var realm1 = engine1.CreateRealm();
        Assert.Throws<JsTerminationException>(() => realm1.Execute("for (let i = 0; i < 10_000; i++) {}"));
        Assert.Equal(10_000, engine2.CreateRealm().Execute("let i = 0; for (; i < 10_000; i++) {} i").AsNumber());

        // The limits of an engine can be changed through its options
        engine1.Options.MaxStatements = null;
        Assert.Equal(10_000, realm1.Execute("let i = 0; for (; i < 10_000; i++) {} i").AsNumber());
    }

    [Fact]
    public void Options_ConfigurationCannotChangeAfterCreation()
    {
        var host = new JsHost();
        var options = new JsEngineOptions { Host = host, TimeZone = "Europe/Paris", DefaultLocale = "fr-FR" };
        using var engine = new JsEngine(options);
        var realm = engine.CreateRealm();
        Assert.Same(host, engine.Options.Host);
        Assert.Equal("Europe/Paris", engine.Options.TimeZone);

        Assert.Throws<InvalidOperationException>(() => engine.Options.Host = null);
        Assert.Throws<InvalidOperationException>(() => engine.Options.TimeZone = "UTC");
        Assert.Throws<InvalidOperationException>(() => engine.Options.DefaultLocale = "en-US");
        Assert.Throws<InvalidOperationException>(() => engine.Options.ModuleLoader = null);
        Assert.Throws<InvalidOperationException>(() => engine.Options.TimeProvider = TimeProvider.System);

        // The time zone of the options given to the constructor can still change: it does not change the engine
        options.TimeZone = "UTC";
        Assert.Equal("Europe/Paris", realm.Execute("Intl.DateTimeFormat().resolvedOptions().timeZone").AsString());
    }

    [Fact]
    public void Options_CopyHasAllTheProperties()
    {
        // A new option must be copied by CreateEngineCopy
        var options = new JsEngineOptions
        {
            RecursionLimit = 12,
            Timeout = TimeSpan.FromSeconds(3),
            MemoryLimit = 4,
            MaxStatements = 5,
            RegExpTimeout = TimeSpan.FromSeconds(6),
            CanBlock = false,
            CancellationToken = new CancellationToken(canceled: true),
            TimeProvider = new FixedTimeProvider(),
            TimeZone = "Europe/Paris",
            DefaultLocale = "fr-FR",
            ModuleLoader = new NoModuleLoader(),
            Host = new JsHost(),
            EnableV8StackTraceApi = true,
            AllowStringCompilation = false,
            EnableCompilation = false,
            CompilationThreshold = 7,
            Debugger = new NoDebugger(),
        };

        using var engine = new JsEngine(options);
        var defaults = new JsEngineOptions();
        foreach (var property in typeof(JsEngineOptions).GetProperties())
        {
            Assert.NotEqual(property.GetValue(defaults), property.GetValue(options));
            Assert.Equal(property.GetValue(options), property.GetValue(engine.Options));
        }
    }

    [Fact]
    public void JsArray_IndexedAccess()
    {
        #region JavaScriptEngine_ArrayIndexer
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var array = (JsArray)realm.Execute("const a = [10, , 30]; Object.defineProperty(a, 3, { get() { return this[0] * 4; }, enumerable: true }); a").AsObject();

        using (engine.Enter())
        {
            // The indexer reads and writes the elements as array[index] does: a hole reads the prototype chain, a getter runs
            Assert.Equal(10, array[0].AsNumber());
            Assert.True(array[1].IsUndefined);
            Assert.Equal(40, array[3].AsNumber());

            // Writing past the end extends the array
            array[5] = "six";
            Assert.Equal(6u, array.Length);

            // ToArray copies the elements: the holes are undefined
            var elements = array.ToArray();
            Assert.Equal("10,undefined,30,40,undefined,six", string.Join(',', elements.Select(element => element.ToString())));
        }
        #endregion

        using (engine.Enter())
        {
            realm.Execute("Array.prototype[1] = 'inherited'");
            Assert.Equal("inherited", array[1].AsString());
            Assert.Equal("inherited", array.ToArray()[1].AsString());

            // The largest array index
            array[4_294_967_294] = 1;
            Assert.Equal(4_294_967_295u, array.Length);
            Assert.Throws<ArgumentOutOfRangeException>(() => array[uint.MaxValue]);
            Assert.Throws<InvalidOperationException>(() => array.ToArray());

            var frozen = (JsArray)realm.Execute("Object.freeze([1])").AsObject();
            var exception = Assert.Throws<JsException>(() => frozen[0] = 2);
            Assert.StartsWith("TypeError", exception.Message);

            // A getter that changes the array during the copy: the length was read before
            var growing = (JsArray)realm.Execute("const g = [1, 2]; Object.defineProperty(g, 0, { get() { g.push(3); g[1] = 20; return 1; } }); g").AsObject();
            Assert.Equal("1,20", string.Join(',', growing.ToArray().Select(element => element.ToString())));
        }

        Assert.Throws<InvalidOperationException>(() => array[0]);
    }

    [Fact]
    public void Symbols_CreateAndRegistry()
    {
        #region JavaScriptEngine_Symbols
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        // A new symbol is a unique property key
        var secret = JsSymbol.Create("secret");
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty(secret, 42);
            realm.GlobalObject.SetProperty("secretKey", secret);
        }

        Assert.Equal(42, realm.Execute("globalThis[secretKey]").AsNumber());
        Assert.Equal("undefined", realm.Execute("typeof globalThis[Symbol('secret')]").AsString());

        // The symbols of the registry are the ones of Symbol.for, in all the realms of the engine
        var shared = engine.GetRegisteredSymbol("app.id");
        Assert.True(shared.IsRegistered);
        Assert.Same(shared, realm.Execute("Symbol.for('app.id')").AsSymbol());
        Assert.Same(shared, engine.CreateRealm().Execute("Symbol.for('app.id')").AsSymbol());
        #endregion

        Assert.False(secret.IsRegistered);
        Assert.NotSame(JsSymbol.Create("secret"), secret);
        Assert.Null(JsSymbol.Create().Description);
        Assert.Equal("app.id", realm.Execute("Symbol.keyFor(Symbol.for('app.id'))").AsString());

        // Another engine has its own registry
        using var otherEngine = new JsEngine();
        Assert.NotSame(shared, otherEngine.GetRegisteredSymbol("app.id"));

        // The well-known symbols
        Assert.Same(JsSymbol.Species, realm.Execute("Symbol.species").AsSymbol());
        Assert.Same(JsSymbol.IsConcatSpreadable, realm.Execute("Symbol.isConcatSpreadable").AsSymbol());
        Assert.Same(JsSymbol.Match, realm.Execute("Symbol.match").AsSymbol());
        Assert.Same(JsSymbol.MatchAll, realm.Execute("Symbol.matchAll").AsSymbol());
        Assert.Same(JsSymbol.Replace, realm.Execute("Symbol.replace").AsSymbol());
        Assert.Same(JsSymbol.Search, realm.Execute("Symbol.search").AsSymbol());
        Assert.Same(JsSymbol.Split, realm.Execute("Symbol.split").AsSymbol());
        Assert.Same(JsSymbol.Dispose, realm.Execute("Symbol.dispose").AsSymbol());
        Assert.Same(JsSymbol.AsyncDispose, realm.Execute("Symbol.asyncDispose").AsSymbol());
        Assert.Same(JsSymbol.Metadata, realm.Execute("Symbol.metadata").AsSymbol());

        // A symbol works as a key in any engine (the objects do not)
        var otherRealm = otherEngine.CreateRealm();
        using (otherEngine.Enter())
        {
            otherRealm.GlobalObject.SetProperty(secret, "other");
        }

        Assert.Equal("other", otherEngine.Call(otherRealm.Execute("(o, k) => o[k]"), JsValue.Undefined, otherRealm.GlobalThis, secret).AsString());
    }

    [Fact]
    public void JsException_JavaScriptStackTrace()
    {
        #region JavaScriptEngine_ExceptionStackTrace
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var exception = Assert.Throws<JsException>(() => realm.Execute("""
            function parse(text) {
              throw new SyntaxError('Unexpected ' + text);
            }
            function load() {
              return parse('}');
            }
            load();
            """, "loader.js"));

        // The JavaScript frames, the innermost first: the first one is where the error was created
        Assert.Equal("""
                at parse (loader.js:2:13)
                at load (loader.js:5:10)
                at loader.js:7:1
            """, exception.JavaScriptStackTrace);
        #endregion

        // ToString includes the JavaScript frames
        Assert.Contains("JavaScript stack trace:", exception.ToString());

        // A thrown primitive has no stack trace
        exception = Assert.Throws<JsException>(() => realm.Execute("function f() { throw 42; } f()"));
        Assert.Null(exception.JavaScriptStackTrace);
        Assert.DoesNotContain("JavaScript stack trace:", exception.ToString());

        // An error created by the host when no script runs has no frames
        using (engine.Enter())
        {
            Assert.Equal("", realm.CreateException(JsErrorType.TypeError, "x").JavaScriptStackTrace);
        }

        // The stack trace is the one captured when the error was created, even when the script replaced its stack property
        exception = Assert.Throws<JsException>(() => realm.Execute("const e = new Error('x'); e.stack = 'custom'; throw e;"));
        Assert.Equal("    at <anonymous>:1:15", exception.JavaScriptStackTrace);
    }

    [Fact]
    public void V8StackTraceApi_IsDisabledByDefault()
    {
        using var engine = new JsEngine();
        Assert.Equal("undefined,undefined", engine.CreateRealm().Execute("[typeof Error.captureStackTrace, typeof Error.stackTraceLimit].join()").AsString());
    }

    [Fact]
    public void V8StackTraceApi_CaptureStackTrace()
    {
        #region JavaScriptEngine_CaptureStackTrace
        using var engine = new JsEngine(new JsEngineOptions { EnableV8StackTraceApi = true });
        var realm = engine.CreateRealm();
        var stack = realm.Execute("""
            class HttpError {
              constructor(status) {
                this.name = 'HttpError';
                this.message = 'Status ' + status;
                // Omit the frames of the constructor
                Error.captureStackTrace(this, HttpError);
              }
            }
            function request() { return new HttpError(404); }
            request().stack
            """, "http.js").AsString();

        Assert.Equal("HttpError: Status 404\n    at request (http.js:9:33)\n    at http.js:10:1", stack);
        #endregion

        Assert.Equal("true,false,true,string", realm.Execute("""
            const target = {};
            Error.captureStackTrace(target);
            const d = Object.getOwnPropertyDescriptor(target, 'stack');
            [d.writable, d.enumerable, d.configurable, typeof d.value].join()
            """).AsString());
        Assert.Equal("10,true,true", realm.Execute("const l = Object.getOwnPropertyDescriptor(Error, 'stackTraceLimit'); [l.value, l.writable, l.enumerable].join()").AsString());
        Assert.Equal("TypeError", realm.Execute("try { Error.captureStackTrace(1); } catch (e) { e.name }").AsString());

        // The limit applies to the errors and to captureStackTrace; a constructor that is not on the stack gives no frame
        Assert.Equal(2, realm.Execute("""
            Error.stackTraceLimit = 1;
            function a() { return new Error('x').stack; }
            function b() { return a(); }
            b().split('\n').length
            """).AsNumber());
        Assert.Equal(1, realm.Execute("Error.stackTraceLimit = 0; (function f() { return new Error('x').stack; })().split('\\n').length").AsNumber());
        Assert.Equal(1, realm.Execute("Error.stackTraceLimit = 'x'; (function f() { return new Error('x').stack; })().split('\\n').length").AsNumber());
        Assert.Equal(1, realm.Execute("Error.stackTraceLimit = 10; const o = {}; (function f() { Error.captureStackTrace(o, function notOnStack() {}); })(); o.stack.split('\\n').length").AsNumber());

        // An error object gets the stack in its internal slot too, and another realm can capture the stack of an object
        Assert.Equal("Error: y\n    at g (<anonymous>:1:48)\n    at <anonymous>:1:72", realm.Execute("const e = new Error('y'); function g() { Error.captureStackTrace(e); } g(); e.stack").AsString());
        var otherRealm = engine.CreateRealm();
        using (engine.Enter())
        {
            otherRealm.GlobalObject.SetProperty("target", realm.CreateObject());
        }

        Assert.Equal("Error\n    at <anonymous>:1:7", otherRealm.Execute("Error.captureStackTrace(target); target.stack").AsString());
        Assert.Equal("Custom: text", realm.Execute("const c = { name: 'Custom', message: 'text' }; Error.stackTraceLimit = 0; Error.captureStackTrace(c); c.stack").AsString());
    }

    [Fact]
    public void AllowStringCompilation_False()
    {
        using var engine = new JsEngine(new JsEngineOptions { AllowStringCompilation = false });
        var realm = engine.CreateRealm();
        Assert.Equal("EvalError,EvalError,EvalError,EvalError,3", realm.Execute("""
            const errors = [];
            try { eval('1'); } catch (e) { errors.push(e.name); }
            try { (0, eval)('1'); } catch (e) { errors.push(e.name); }
            try { new Function('return 1'); } catch (e) { errors.push(e.name); }
            try { new ShadowRealm().evaluate('1'); } catch (e) { errors.push(e.constructor.name); }
            // Code that is not given as a string runs
            errors.push([1, 2].map(x => x + 1).at(-1));
            errors.join()
            """).AsString());
    }

    [Fact]
    public void Marshalling_FromDotNet()
    {
        #region JavaScriptEngine_FromDotNet
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            var order = new Dictionary<string, object?>
            {
                ["id"] = "A-12",
                ["date"] = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero),
                ["lines"] = new[]
                {
                    new Dictionary<string, object?> { ["sku"] = "pen", ["quantity"] = 2, ["price"] = 1.5m },
                    new Dictionary<string, object?> { ["sku"] = "ink", ["quantity"] = 1, ["price"] = 4m },
                },
                ["notes"] = null,
            };

            realm.GlobalObject.SetProperty("order", realm.FromDotNet(order));
        }

        Assert.Equal("A-12,2024,7,true", realm.Execute("""
            [order.id, order.date.getUTCFullYear(), order.lines.reduce((sum, line) => sum + line.quantity * line.price, 0), order.notes === null].join()
            """).AsString());
        #endregion

        using (engine.Enter())
        {
            Assert.True(realm.FromDotNet(null).IsNull);
            Assert.Equal(42, realm.FromDotNet(42L).AsNumber());
            Assert.Equal("x", realm.FromDotNet('x').AsString());
            Assert.Equal(new System.Numerics.BigInteger(5), realm.FromDotNet(new System.Numerics.BigInteger(5)).AsBigInt());
            Assert.True(realm.FromDotNet<bool>(true).AsBoolean());
            Assert.Equal(1.5, realm.FromDotNet<double>(1.5).AsNumber());

            var json = System.Text.Json.JsonDocument.Parse("""{ "a": [1, "two", true, null, { "b": 2.5 }] }""").RootElement;
            realm.GlobalObject.SetProperty("fromElement", realm.FromDotNet(json));
            realm.GlobalObject.SetProperty("fromNode", realm.FromDotNet(System.Text.Json.Nodes.JsonNode.Parse("""{ "a": [1, "two", true, null, { "b": 2.5 }] }""")));

            var cyclic = new List<object?>();
            cyclic.Add(cyclic);
            Assert.Throws<InvalidOperationException>(() => realm.FromDotNet(cyclic));
            Assert.Throws<NotSupportedException>(() => realm.FromDotNet(new object()));
            Assert.Throws<NotSupportedException>(() => realm.FromDotNet(new Func<int>(() => 1)));
            Assert.Throws<NotSupportedException>(() => realm.FromDotNet(new Dictionary<int, int> { [1] = 1 }));

            // The same value can appear twice: only cycles are rejected
            var shared = new[] { 1 };
            Assert.Equal(2u, ((JsArray)realm.FromDotNet(new[] { shared, shared }).AsObject()).Length);
        }

        Assert.Equal("""{"a":[1,"two",true,null,{"b":2.5}]}|{"a":[1,"two",true,null,{"b":2.5}]}""", realm.Execute("JSON.stringify(fromElement) + '|' + JSON.stringify(fromNode)").AsString());
        Assert.Throws<InvalidOperationException>(() => realm.FromDotNet(1));
    }

    [Fact]
    public void Marshalling_ToDotNet()
    {
        #region JavaScriptEngine_ToDotNet
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var value = realm.Execute("({ name: 'pen', tags: ['blue', 'cheap'], price: 1.5, stock: null, created: new Date(0) })");

        // Objects are dictionaries, arrays are lists, numbers are doubles, dates are DateTimeOffset
        var product = (Dictionary<string, object?>)engine.ToDotNet(value)!;
        Assert.Equal("pen", product["name"]);
        Assert.Equal(["blue", "cheap"], (List<object?>)product["tags"]!);
        Assert.Equal(1.5, product["price"]);
        Assert.Null(product["stock"]);
        Assert.Equal(DateTimeOffset.UnixEpoch, product["created"]);

        // A typed conversion follows the rules of JavaScript, and checks the range of the integers
        Assert.Equal(12, engine.ToDotNet<int>(realm.Execute("'12'")));
        Assert.Equal("12", engine.ToDotNet<string>(realm.Execute("12")));
        Assert.Null(engine.ToDotNet<int?>(JsValue.Undefined));
        #endregion

        Assert.Throws<JsException>(() => engine.ToDotNet<int>(realm.Execute("1.5")));
        Assert.Throws<JsException>(() => engine.ToDotNet<byte>(realm.Execute("256")));
        Assert.Throws<JsException>(() => engine.ToDotNet<int>(JsValue.Undefined));
        Assert.Throws<JsException>(() => engine.ToDotNet<DateTime>(realm.Execute("new Date(NaN)")));
        Assert.Throws<JsException>(() => engine.ToDotNet<JsObject>(realm.Execute("1")));
        Assert.Throws<NotSupportedException>(() => engine.ToDotNet<Guid>(JsValue.Undefined));
        Assert.False(engine.ToDotNet<bool>(JsValue.Undefined));
        Assert.Equal(3_000_000_000L, engine.ToDotNet<long>(realm.Execute("3e9")));
        Assert.Equal(1.25m, engine.ToDotNet<decimal>(realm.Execute("1.25")));
        Assert.Equal(new System.Numerics.BigInteger(10), engine.ToDotNet<System.Numerics.BigInteger>(realm.Execute("10n")));
        Assert.Equal(DateTime.UnixEpoch, engine.ToDotNet<DateTime>(realm.Execute("new Date(0)")));
        Assert.Throws<NotSupportedException>(() => engine.ToDotNet(realm.Execute("[Symbol()]")));
        Assert.Throws<NotSupportedException>(() => engine.ToDotNet(realm.Execute("({ f() {} })")));
        Assert.Throws<InvalidOperationException>(() => engine.ToDotNet(realm.Execute("const c = {}; c.self = c; c")));

        // The getters run, the symbol keys and the non-enumerable properties are skipped
        var result = (Dictionary<string, object?>)engine.ToDotNet(realm.Execute("const r = { get a() { return 1; }, [Symbol()]: 2 }; Object.defineProperty(r, 'hidden', { value: 3 }); r"))!;
        Assert.Equal(["a"], result.Keys.ToArray());
    }

    [Fact]
    public void Marshalling_TypedFunctions()
    {
        #region JavaScriptEngine_TypedFunctions
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var log = new List<string>();
        using (engine.Enter())
        {
            // The arguments are converted to the types of the delegate, and the result to a JavaScript value
            realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", (int a, int b) => a + b));
            realm.GlobalObject.SetProperty("repeat", realm.CreateFunction("repeat", (string text, int? count) => string.Concat(Enumerable.Repeat(text, count ?? 2))));
            realm.GlobalObject.SetProperty("log", realm.CreateFunction("log", (string message) => log.Add(message)));
        }

        Assert.Equal(7, realm.Execute("add(3, '4')").AsNumber());
        Assert.Equal("abab", realm.Execute("repeat('ab')").AsString());
        realm.Execute("log('hello')");
        Assert.Equal(["hello"], log);

        // An argument that cannot be converted is a TypeError (or a RangeError) of the script
        Assert.Equal("TypeError: 1.5 is not an integer", realm.Execute("try { add(1.5, 1); } catch (e) { `${e}` }").AsString());
        #endregion

        Assert.Equal("RangeError", realm.Execute("try { add(2 ** 40, 1); } catch (e) { e.name }").AsString());
        Assert.Equal("TypeError", realm.Execute("try { add(1); } catch (e) { e.name }").AsString());
        Assert.Equal("add,2", realm.Execute("[add.name, add.length].join()").AsString());
        using (engine.Enter())
        {
            Assert.Throws<NotSupportedException>(() => realm.CreateFunction("f", (Guid id) => id));
            realm.GlobalObject.SetProperty("now", realm.CreateFunction("now", () => new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            realm.GlobalObject.SetProperty("keys", realm.CreateFunction("keys", (object? value) => value is Dictionary<string, object?> dictionary ? string.Join(',', dictionary.Keys) : "?"));
            realm.GlobalObject.SetProperty("noop", realm.CreateFunction("noop", () => { }));
            realm.GlobalObject.SetProperty("sum3", realm.CreateFunction("sum3", (double a, double b, double c) => a + b + c));
            realm.GlobalObject.SetProperty("pair", realm.CreateFunction("pair", (string a, JsValue b) => log.Add(a + b.Kind)));
        }

        Assert.Equal(2000, realm.Execute("now().getUTCFullYear()").AsNumber());
        Assert.Equal("a,b", realm.Execute("keys({ a: 1, b: 2 })").AsString());
        Assert.True(realm.Execute("noop()").IsUndefined);
        Assert.Equal(6, realm.Execute("sum3(1, 2, 3)").AsNumber());
        realm.Execute("pair('x', 1)");
        Assert.Equal("xNumber", log[^1]);

        // A .NET exception of the delegate is not a JavaScript error
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("fail", realm.CreateFunction("fail", () => throw new InvalidOperationException("boom")));
        }

        Assert.Throws<InvalidOperationException>(() => realm.Execute("try { fail(); } catch { }"));
    }

    [Fact]
    public async Task Post_RunsTheWorkOnTheEngineThread()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute("globalThis.log = []");
        var log = realm.Execute("log");

        // Work posted from other threads while the engine runs and while it is idle
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("block", realm.CreateFunction("block", 0, (_, _) =>
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                return JsValue.Undefined;
            }));
        }

        var running = Task.Run(() => realm.Execute("block(); log.push('script'); Promise.resolve().then(() => log.push('microtask'));"));
        Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
        await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Task.Run(() => Assert.True(engine.Post(() => engine.Call(realm.Execute("v => log.push(v)"), JsValue.Undefined, "posted" + i.ToString(CultureInfo.InvariantCulture)), realm)))));
        release.Set();
        await running;

        // The posted work runs from RunJobs, after the promise jobs already queued, in the order it was posted
        Assert.True(engine.Post(() => realm.Execute("log.push('idle'); Promise.resolve().then(() => log.push('after idle'))")));
        engine.RunJobs();
        Assert.Equal("script,microtask", engine.ToString(engine.Call(realm.Execute("() => log.slice(0, 2)"), JsValue.Undefined)));
        Assert.Equal("idle,after idle", engine.ToString(engine.Call(realm.Execute("() => log.slice(-2)"), JsValue.Undefined)));
        Assert.Equal(7, engine.Call(realm.Execute("() => log.length"), JsValue.Undefined).AsNumber());
        GC.KeepAlive(log);

        // A JsException goes to the host (rethrown by default), another exception comes out of RunJobs
        Assert.True(engine.Post(() => throw new JsException("job failed")));
        Assert.Equal("job failed", Assert.Throws<JsException>(engine.RunJobs).Message);
        Assert.True(engine.Post(() => throw new InvalidOperationException("bug")));
        Assert.Throws<InvalidOperationException>(engine.RunJobs);

        // Once the engine is disposed, the work is not accepted
        engine.Dispose();
        Assert.False(engine.Post(() => throw new UnreachableException()));
    }

    [Fact]
    public async Task WaitForJobsAsync_CompletesWhenWorkIsPosted()
    {
        using var engine = new JsEngine();
        engine.CreateRealm();

        // Nothing is queued: the wait completes when work is posted
        var wait = engine.WaitForJobsAsync();
        Assert.False(wait.IsCompleted);
        var ran = false;
        _ = Task.Run(() => engine.Post(() => ran = true));
        await wait.WaitAsync(TimeSpan.FromSeconds(30));
        engine.RunJobs();
        Assert.True(ran);

        // Cancellation, and disposal
        using var cancellation = new CancellationTokenSource();
        wait = engine.WaitForJobsAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);

        wait = engine.WaitForJobsAsync();
        engine.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => wait.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Tasks_AsyncHostFunction()
    {
        #region JavaScriptEngine_TaskPromises
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            // A .NET function returns a promise of a task: the engine settles it on its own thread when the task completes
            realm.GlobalObject.SetProperty("readText", realm.CreateFunction("readText", 1, (_, arguments) =>
            {
                var name = engine.ToDotNet<string>(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
                return realm.CreatePromise(ReadTextAsync(name), text => text);
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
            """);

        // Run the jobs and wait for the tasks until the promise settles
        var result = await engine.RunUntilSettledAsync(promise);
        Assert.Equal("content of a.txt | missing.txt not found", result.AsString());

        static async Task<string> ReadTextAsync(string name)
        {
            await Task.Delay(10);
            return name is "missing.txt" ? throw new FileNotFoundException(name + " not found") : "content of " + name;
        }
        #endregion
    }

    [Fact]
    public async Task Tasks_PromiseSettlement()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();

        // Completed tasks settle the promise at once
        using (engine.Enter())
        {
            Assert.True(realm.CreatePromise(Task.CompletedTask).TryGetPromiseState(out var state, out var value, out _));
            Assert.Equal(JsPromiseState.Fulfilled, state);
            Assert.True(value.IsUndefined);

            Assert.True(realm.CreatePromise(Task.FromResult(new JsValue(3))).TryGetPromiseState(out state, out value, out _));
            Assert.Equal(3, value.AsNumber());

            Assert.True(realm.CreatePromise(Task.FromException<JsValue>(new JsException(new JsValue(7)))).TryGetPromiseState(out state, out value, out _));
            Assert.Equal(JsPromiseState.Rejected, state);
            Assert.Equal(7, value.AsNumber());

            Assert.True(realm.CreatePromise(Task.FromCanceled(new CancellationToken(canceled: true))).TryGetPromiseState(out state, out value, out _));
            Assert.Equal(JsPromiseState.Rejected, state);
            Assert.Equal("Error: A task was canceled.", engine.ToString(value));

            Assert.True(realm.CreatePromise(Task.FromResult(1), _ => throw new JsException("bad result")).TryGetPromiseState(out state, out value, out _));
            Assert.Equal(JsPromiseState.Rejected, state);
            Assert.Equal("Error: bad result", engine.ToString(value));
        }

        // A task that completes after the entry that created the promise returned
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        JsValue promise;
        using (engine.Enter())
        {
            promise = realm.CreatePromise(completion.Task, value => value * 2);
        }

        _ = Task.Run(() => completion.SetResult(21));
        Assert.Equal(42, (await engine.RunUntilSettledAsync(promise).WaitAsync(TimeSpan.FromSeconds(30))).AsNumber());

        // A rejected JavaScript promise faults the task with a JsException
        var exception = await Assert.ThrowsAsync<JsException>(() => engine.RunUntilSettledAsync(realm.Execute("Promise.reject(new RangeError('no'))")));
        Assert.Equal("RangeError: no", exception.Message);

        // ToTask of a value that is not a promise
        var task = engine.ToTask(5);
        engine.RunJobs();
        Assert.Equal(5, (await task).AsNumber());

        // The pending tasks are canceled when the engine is disposed
        task = engine.ToTask(realm.Execute("new Promise(() => {})"));
        engine.Dispose();
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.RunUntilSettledAsync(5));
    }

    [Fact]
    public async Task Tasks_TimeoutJobsWakeTheEventLoop()
    {
        // Atomics.waitAsync uses a timeout job: RunUntilSettledAsync waits for its delay
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var promise = realm.Execute("Atomics.waitAsync(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 20).value");
        Assert.Equal("timed-out", (await engine.RunUntilSettledAsync(promise).WaitAsync(TimeSpan.FromSeconds(30))).AsString());
    }

    [Fact]
    public void UnhandledRejections_AreReportedAfterTheJobs()
    {
        #region JavaScriptEngine_UnhandledRejectionEvent
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var unhandled = new List<string>();
        var handledLater = new List<string>();
        engine.UnhandledRejection += (_, e) => unhandled.Add(engine.ToString(e.Reason));
        engine.RejectionHandled += (_, e) => handledLater.Add(engine.ToString(e.Reason));

        realm.Execute("""
            Promise.reject(new Error('caught')).catch(() => {});
            Promise.resolve().then(() => { globalThis.forgotten = Promise.reject(new Error('forgotten')); });
            """);

        // The rejections are reported once the jobs have run: a handler attached by a job in the meantime prevents it
        engine.RunJobs();
        Assert.Equal(["Error: forgotten"], unhandled);

        // A handler attached later is reported too
        realm.Execute("forgotten.catch(() => {})");
        engine.RunJobs();
        Assert.Equal(["Error: forgotten"], handledLater);
        #endregion

        // A rejection handled by a job of the same checkpoint is not reported
        realm.Execute("const p = Promise.reject(1); Promise.resolve().then(() => p.catch(() => {}));");
        engine.RunJobs();
        Assert.Single(unhandled);
    }

    [Fact]
    public void UntrustedCodeConfiguration()
    {
        #region JavaScriptEngine_UntrustedCode
        // The budget of the whole work (the script and its jobs): the limits of the options apply to each entry
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var engine = new JsEngine(new JsEngineOptions
        {
            Timeout = TimeSpan.FromMilliseconds(500),
            MaxStatements = 10_000_000,
            MemoryLimit = 64 * 1024 * 1024,
            RegExpTimeout = TimeSpan.FromMilliseconds(100),
            RecursionLimit = 1_000,
            CancellationToken = cancellation.Token,
            AllowStringCompilation = false,
        });
        var realm = engine.CreateRealm();

        // A catastrophic regular expression is stopped by RegExpTimeout
        var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("/^(a+)+$/.test('a'.repeat(40) + '!')"));
        Assert.Equal(JsTerminationReason.Timeout, exception.Reason);

        // Each job is short, but the chain of jobs never ends: only the cancellation of the whole work stops it
        realm.Execute("(function spin() { Promise.resolve().then(spin); })()");
        exception = Assert.Throws<JsTerminationException>(engine.RunJobs);
        Assert.Equal(JsTerminationReason.Canceled, exception.Reason);
        #endregion
    }

    [Fact]
    public void Export_Class()
    {
        #region JavaScriptEngine_ExportClass
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            // The constructor of the JavaScript class of Counter, created once per realm
            realm.GlobalObject.SetProperty("Counter", realm.GetClass(Counter.JsClass));
        }

        Assert.Equal(3, realm.Execute("const counter = new Counter(1); counter.increment(); counter.increment(); counter.value").AsNumber());
        Assert.Equal("Counter(13)", realm.Execute("counter.value += 10; `${counter}`").AsString());
        Assert.Equal(5, realm.Execute("Counter.parse('5').value").AsNumber());
        Assert.True(realm.Execute("counter.reset === undefined").AsBoolean());

        // The scripts can extend the class
        Assert.Equal(15, realm.Execute("class Double extends Counter { increment() { return super.increment(2); } } new Double(13).increment()").AsNumber());

        // An argument that cannot be converted is a TypeError of the script
        Assert.Equal("TypeError: 1.5 is not an integer", realm.Execute("try { counter.increment(1.5); } catch (e) { `${e}` }").AsString());

        // The .NET object of a JavaScript object, and the JavaScript object of a .NET object
        var counter = engine.ToDotNet<Counter>(realm.Execute("counter"));
        Assert.Equal(13, counter.Value);
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("sameCounter", realm.FromDotNet(counter));
        }

        Assert.True(realm.Execute("sameCounter === counter").AsBoolean());
        #endregion

        Assert.True(realm.Execute("counter.clone() !== counter && counter.clone() instanceof Counter && counter.clone().value === 13").AsBoolean());
        Assert.Equal("function|Counter|0", realm.Execute("[typeof Counter, Counter.name, Counter.length].join('|')").AsString());
        Assert.Equal("increment|0|get value|set value", realm.Execute("""
            const descriptor = Object.getOwnPropertyDescriptor(Counter.prototype, 'value');
            [Counter.prototype.increment.name, Counter.prototype.increment.length, descriptor.get.name, descriptor.set.name].join('|')
            """).AsString());
        Assert.Equal("clone,constructor,increment,incrementLaterAsync,toString,value", realm.Execute("Object.getOwnPropertyNames(Counter.prototype).sort().join()").AsString());
        Assert.Equal("length,name,parse,prototype", realm.Execute("Object.getOwnPropertyNames(Counter).sort().join()").AsString());
        Assert.Equal("false|true|true|false|0", realm.Execute("""
            const method = Object.getOwnPropertyDescriptor(Counter.prototype, 'increment');
            const prototype = Object.getOwnPropertyDescriptor(Counter, 'prototype');
            [method.enumerable, method.writable, method.configurable, prototype.writable, Object.keys(counter).length].join('|')
            """).AsString());
        Assert.True(realm.Execute("Counter.prototype.constructor === Counter && Object.getPrototypeOf(counter) === Counter.prototype").AsBoolean());
        Assert.Equal("TypeError: Class constructor Counter cannot be invoked without 'new'", realm.Execute("try { Counter(); } catch (e) { `${e}` }").AsString());
        Assert.Equal("TypeError: Illegal invocation", realm.Execute("try { Counter.prototype.increment.call({}); } catch (e) { `${e}` }").AsString());
        Assert.Equal("TypeError", realm.Execute("try { Object.getOwnPropertyDescriptor(Counter.prototype, 'value').get.call(1); } catch (e) { e.name }").AsString());
        Assert.Equal(9, realm.Execute("const fromDefault = new Counter(); fromDefault.increment(); fromDefault.increment(undefined) + fromDefault.increment(5)").AsNumber());

        // The class is created once per realm
        using (engine.Enter())
        {
            Assert.Same(realm.GetClass(Counter.JsClass), realm.GetClass(Counter.JsClass));
            Assert.NotSame(realm.GetClass(Counter.JsClass), engine.CreateRealm().GetClass(Counter.JsClass));
            Assert.Equal("Counter", Counter.JsClass.Name);
            Assert.Equal(typeof(Counter), Counter.JsClass.Type);
            Assert.False(Counter.JsClass.IsStatic);
            Assert.Null(Counter.JsClass.BaseClass);
        }
    }

    [Fact]
    public void Export_StaticClass()
    {
        #region JavaScriptEngine_ExportStaticClass
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            // The object of a static class
            realm.GlobalObject.SetProperty("app", realm.GetClass(AppApi.JsClass));
        }

        Assert.Equal("Hello, Ada!", realm.Execute("app.greet('Ada')").AsString());
        Assert.Equal("Hi, Ada!", realm.Execute("app.greet('Ada', 'Hi')").AsString());
        Assert.Equal("1.0", realm.Execute("app.version").AsString());
        Assert.Equal(5, realm.Execute("const point = app.createPoint(3, 4); Math.hypot(point.x, point.y)").AsNumber());

        // A parameter whose type is a non-nullable reference type requires a value
        Assert.Equal("TypeError: A value is required: undefined cannot be converted", realm.Execute("try { app.greet(); } catch (e) { `${e}` }").AsString());
        #endregion

        Assert.Equal("object|[object App]|2|1", realm.Execute("[typeof app, Object.prototype.toString.call(app), app.createPoint.length, app.greet.length].join('|')").AsString());
        Assert.Equal("TypeError", realm.Execute("try { app.greet(null); } catch (e) { e.name }").AsString());
        Assert.True(AppApi.JsClass.IsStatic);
    }

    [Fact]
    public void Export_Inheritance()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("ExportedSquare", realm.GetClass(ExportedSquare.JsClass));
            Assert.Same(realm.GetClass(ExportedShape.JsClass), realm.GetClass(ExportedSquare.JsClass).GetPrototype());
            Assert.Same(ExportedShape.JsClass, ExportedSquare.JsClass.BaseClass);
        }

        // The methods of the base class call the overrides of the .NET class
        Assert.Equal("9|square 9|square|3", realm.Execute("const square = new ExportedSquare(3); [square.area(), square.describe(), square.baseName(), square.side].join('|')").AsString());
        Assert.Equal(16, realm.Execute("square.side = 4; square.area()").AsNumber());
        Assert.True(realm.Execute("""
            const Shape = Object.getPrototypeOf(ExportedSquare);
            Shape.name === 'ExportedShape' && square instanceof Shape && square instanceof ExportedSquare
              && Object.hasOwn(Shape.prototype, 'area') && !Object.hasOwn(ExportedSquare.prototype, 'area')
              && Object.hasOwn(ExportedSquare.prototype, 'side') && typeof ExportedSquare.count === 'number'
            """).AsBoolean());
        Assert.Equal("base static", realm.Execute("ExportedSquare.baseStatic()").AsString());

        // An abstract class cannot be created by the scripts
        Assert.Equal("TypeError: Illegal constructor", realm.Execute("try { new Shape(); } catch (e) { `${e}` }").AsString());

        // A .NET object is an object of the class of its runtime type
        using (engine.Enter())
        {
            ExportedShape shape = new ExportedSquare(2);
            realm.GlobalObject.SetProperty("shape", realm.FromDotNet(shape));
        }

        Assert.True(realm.Execute("shape instanceof ExportedSquare && shape.area() === 4").AsBoolean());

        // A member of a base class is called through the base class: an ignored or non-public member of the derived
        // class with the same name hides it from the scripts, or is not called instead of it
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("ExportedHiding", realm.GetClass(ExportedHiding.JsClass));
            realm.GlobalObject.SetProperty("ExportedSelfPublishing", realm.GetClass(ExportedSelfPublishing.JsClass));
        }

        Assert.Equal("undefined|base", realm.Execute("const hiding = new ExportedHiding(); [typeof hiding.hidden, hiding.shadowed()].join('|')").AsString());

        // An override with [JsName] names the member of a base class that is not exported, not its overloads
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("ExportedRenaming", realm.GetClass(ExportedRenaming.JsClass));
        }

        Assert.Equal("undefined|derived|x:derived", realm.Execute("const renaming = new ExportedRenaming(); [typeof renaming.describe, renaming.renamed(), renaming.describeWith('x')].join('|')").AsString());

        // A renamed property keeps the accessor that its override inherits
        Assert.Equal("undefined|3|4", realm.Execute("renaming.renamedValue = 3; renaming.renamedStored = 2; [typeof renaming.value, renaming.renamedValue, renaming.renamedStored].join('|')").AsString());

        // new returns the object that the .NET constructor created for itself, with the prototype of new.target
        Assert.Equal("true,true,true,true", realm.Execute("""
            const published = new ExportedSelfPublishing();
            const first = published === during && published.mark === 1;
            class SubPublishing extends ExportedSelfPublishing { }
            const sub = new SubPublishing();
            [first, sub === during, Object.getPrototypeOf(sub) === SubPublishing.prototype, sub instanceof ExportedSelfPublishing].join()
            """).AsString());
    }

    [Fact]
    public void Export_Conversions()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("Conversions", realm.GetClass(ExportedConversions.JsClass));
            realm.GlobalObject.SetProperty("Counter", realm.GetClass(Counter.JsClass));
        }

        realm.Execute("const c = new Conversions('ignored')");
        Assert.True(realm.Execute("c.name === null && Conversions.length === 0").AsBoolean());
        Assert.Equal("Number|null|String", realm.Execute("c.describe(1, null, 'x')").AsString());
        Assert.Equal("Undefined|object|null", realm.Execute("c.describe(undefined, {}, undefined)").AsString());
        Assert.Equal("1,null,2,NaN,1.5,10,1.25,-2,True,null,5", realm.Execute("c.numbers(1, undefined, 2)").AsString());
        Assert.Equal("9007199254740991,1.5,255,1,2,3,4,5,False,j,7", realm.Execute("c.numbers(2 ** 53 - 1, 1.5, 255, 1, 2, 3, 4, 5, false, 'j', 7)").AsString());
        Assert.Equal("RangeError", realm.Execute("try { c.numbers(1, 2, 256); } catch (e) { e.name }").AsString());
        Assert.Equal("RangeError|RangeError", realm.Execute("[2 ** 96, -(2 ** 96)].map(value => { try { c.numbers(1, value, 1); } catch (e) { return e.name; } }).join('|')").AsString());
        Assert.StartsWith("792281625142643", realm.Execute("c.numbers(1, 2 ** 96 - 2 ** 43, 1).split(',')[1]").AsString(), StringComparison.Ordinal);
        Assert.Equal("0|null", realm.Execute("c.dates(new Date(0))").AsString());
        Assert.Equal("0|2001", realm.Execute("c.dates(new Date(0), new Date(Date.UTC(2001, 0, 1)))").AsString());

        // A Date outside the range of DateTimeOffset, and a value that cannot be converted to object, are errors of the script
        Assert.Equal("RangeError|RangeError|TypeError", realm.Execute("""
            [new Date(8.64e15), new Date(-8.64e15), 'not a date'].map(date => { try { c.dates(date); } catch (e) { return e.name; } }).join('|')
            """).AsString());
        Assert.Equal("TypeError|TypeError|TypeError", realm.Execute("""
            const cycle = {};
            cycle.self = cycle;
            [() => 1, cycle, Symbol()].map(value => { try { c.describe(1, null, value); } catch (e) { return e.name; } }).join('|')
            """).AsString());
        Assert.Equal("bigint|9", realm.Execute("const big = c.square(3n); `${typeof big}|${big}`").AsString());
        Assert.Equal("a2", realm.Execute("c.required('a', new Counter(2))").AsString());
        Assert.Equal("TypeError", realm.Execute("try { c.required('a', {}); } catch (e) { e.name }").AsString());
        Assert.Equal("TypeError", realm.Execute("try { c.required(undefined, new Counter()); } catch (e) { e.name }").AsString());
        Assert.True(realm.Execute("const k = new Counter(); c.optionalCounter(null) === null && c.optionalCounter(k) === k").AsBoolean());

        // A non-nullable parameter with a default value gets it for a missing or undefined argument, and rejects null
        Assert.Equal("x|x|y|TypeError", realm.Execute("[c.withDefault(), c.withDefault(undefined), c.withDefault('y'), (() => { try { c.withDefault(null); } catch (e) { return e.name; } })()].join('|')").AsString());
        Assert.Equal("true|1,2,3|a,b|1|x", realm.Execute("[Array.isArray(c.array()), c.array().join(), c.list().join(), c.dictionary().a, c.char()].join('|')").AsString());
        Assert.Equal("v|TypeError", realm.Execute("""
            c.set('v');
            const value = c.lastValue;
            c.lastValue = 'ignored';
            [value, (() => { 'use strict'; try { c.lastValue = 'w'; } catch (e) { return e.name; } })()].join('|')
            """).AsString());
        Assert.True(realm.Execute("c.name = 'n'; const named = c.name === 'n'; c.name = null; named && c.name === null").AsBoolean());
        Assert.Equal("TypeError", realm.Execute("try { c.requiredName = null; } catch (e) { e.name }").AsString());
        Assert.Equal("5|42|7|3|class", realm.Execute("c.field = 5; [c.field, c.readOnlyField, Conversions.constant, c.initOnly, c.class()].join('|')").AsString());

        // A .NET exception of a member is not a JavaScript error
        Assert.Throws<InvalidOperationException>(() => realm.Execute("try { c.throw(); } catch { }"));

        var conversions = engine.ToDotNet<ExportedConversions>(realm.Execute("c"));
        Assert.Equal(5, conversions.Field);
    }

    [Fact]
    public async Task Export_Tasks()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("Conversions", realm.GetClass(ExportedConversions.JsClass));
            realm.GlobalObject.SetProperty("Counter", realm.GetClass(Counter.JsClass));
        }

        var promise = realm.Execute("""
            const c = new Conversions();
            Promise.all([new Counter(1).incrementLaterAsync(), c.valueAsync(), c.completeAsync()]).then(values => values.join())
            """);
        var result = await engine.RunUntilSettledAsync(promise);
        Assert.Equal("2,value,", result.AsString());
    }

    [Fact]
    public void Export_Marshalling()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var counter = new Counter(4);
        JsValue first;
        using (engine.Enter())
        {
            // FromDotNet creates the class of an exported .NET object, and always returns the same JavaScript object for it
            first = realm.FromDotNet(counter);
            Assert.True(JsEngine.SameValue(first, realm.FromDotNet(counter)));
            Assert.True(JsEngine.SameValue(first, realm.FromDotNet(new object[] { counter }).AsObject().GetProperty("0")));
            Assert.False(JsEngine.SameValue(first, engine.CreateRealm().FromDotNet(counter)));

            // The typed functions accept and return the exported classes
            realm.GlobalObject.SetProperty("counter", first);
            realm.GlobalObject.SetProperty("twice", realm.CreateFunction("twice", (Counter value) => value.Increment(value.Value)));
            realm.GlobalObject.SetProperty("make", realm.CreateFunction("make", (int value) => new Counter(value)));
            realm.GlobalObject.SetProperty("NotConstructible", realm.GetClass(ExportedNotConstructible.JsClass));
            realm.GlobalObject.SetProperty("Nested", realm.GetClass(ExportedContainer.Nested.JsClass));
        }

        Assert.Equal(8, realm.Execute("twice(counter)").AsNumber());
        Assert.Equal(8, counter.Value);
        Assert.Equal("Counter(2)", realm.Execute("String(make(2))").AsString());
        Assert.Same(counter, engine.ToDotNet(realm.Execute("counter")));
        Assert.Same(counter, engine.ToDotNet<Counter>(first));
        Assert.Null(engine.ToDotNet<Counter>(JsValue.Null));
        Assert.Throws<JsException>(() => engine.ToDotNet<Counter>(realm.Execute("({})")));
        Assert.Throws<JsException>(() => engine.ToDotNet(realm.Execute("new Date(-8.64e15)")));
        Assert.Throws<JsException>(() => engine.ToDotNet<DateTimeOffset>(realm.Execute("new Date(8.64e15)")));
        Assert.Equal("TypeError", realm.Execute("try { twice({}); } catch (e) { e.name }").AsString());

        // A class without public constructor cannot be created by the scripts
        Assert.Equal("TypeError: Illegal constructor", realm.Execute("try { new NotConstructible(); } catch (e) { `${e}` }").AsString());
        Assert.Equal("hello|Nested|nested", realm.Execute("[NotConstructible.create().hello(), Nested.name, new Nested().hello()].join('|')").AsString());
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoDebugger : JsDebugger
    {
    }

    private sealed class NoModuleLoader : JsModuleLoader
    {
        protected internal override void LoadImportedModule(JsModuleLoadRequest request) => throw new NotSupportedException();
    }
}

#region JavaScriptEngine_ExportClassDeclaration
// The source generator creates the JavaScript class: the class must be partial
[JsExport]
public sealed partial class Counter
{
    // The constructor of the JavaScript class: "new Counter()" or "new Counter(5)"
    public Counter(int start = 0) => Value = start;

    // An accessor of the prototype, named in camelCase: "counter.value"
    public int Value { get; set; }

    // A method of the prototype: "counter.increment(2)"
    public int Increment(int step = 1) => Value += step;

    // An instance of an exported class is converted to its JavaScript object
    public Counter Clone() => new(Value);

    [JsName("toString")]
    public override string ToString() => "Counter(" + Value.ToString(CultureInfo.InvariantCulture) + ")";

    [JsIgnore]
    public void Reset() => Value = 0;

    // A static member is a property of the constructor: "Counter.parse('5')"
    public static Counter Parse(string text) => new(int.Parse(text, CultureInfo.InvariantCulture));

    // A task is a promise
    public async Task<int> IncrementLaterAsync()
    {
        await Task.Yield();
        return Increment();
    }
}
#endregion

#region JavaScriptEngine_ExportStaticClassDeclaration
// A static class is a plain object, as Math
[JsExport(Name = "App")]
public static partial class AppApi
{
    public const string Version = "1.0";

    public static string Greet(string name, string greeting = "Hello") => greeting + ", " + name + "!";

    // A JsRealm parameter receives the realm of the function: it is not an argument of the script
    public static JsObject CreatePoint(JsRealm realm, double x, double y)
    {
        var point = realm.CreateObject();
        point.SetProperty("x", x);
        point.SetProperty("y", y);
        return point;
    }
}
#endregion

[JsExport]
public abstract partial class ExportedShape
{
    public abstract double Area();

    public virtual string Describe() => GetType().Name + " " + Area().ToString(CultureInfo.InvariantCulture);

    public static int Count { get; set; }
}

public abstract class ExportedSquareBase : ExportedShape
{
    public double Side { get; set; }

    public string BaseName() => "square base";

    public static string BaseStatic() => "base static";
}

[JsExport]
public sealed partial class ExportedSquare : ExportedSquareBase
{
    public ExportedSquare(double side) => Side = side;

    public override double Area() => Side * Side;

    public override string Describe() => "square " + Area().ToString(CultureInfo.InvariantCulture);

    public new string BaseName() => "square";
}

[JsExport]
internal sealed partial class ExportedConversions
{
    [JsConstructor]
    public ExportedConversions()
    {
    }

    public ExportedConversions(string name) => Name = name;

    public string? Name { get; set; }

    public string RequiredName { get; set; } = "";

    public string? LastValue { get; private set; }

    public int Field;

    public readonly int ReadOnlyField = 42;

    public const int Constant = 7;

    public int InitOnly { get; init; } = 3;

    public string Describe(JsValue value, JsObject? obj, object? any) => value.Kind + "|" + (obj is null ? "null" : "object") + "|" + (any?.GetType().Name ?? "null");

    public string Numbers(long a, decimal? b, byte c, double d = double.NaN, float e = 1.5f, ulong f = 10, decimal g = 1.25m, short h = -2, bool i = true, string? j = null, int? k = 5)
        => string.Join(',', a, b?.ToString(CultureInfo.InvariantCulture) ?? "null", c, d.ToString(CultureInfo.InvariantCulture), e.ToString(CultureInfo.InvariantCulture), f, g.ToString(CultureInfo.InvariantCulture), h, i, j ?? "null", k);

    public string Dates(DateTimeOffset date, DateTime? other) => date.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "|" + (other?.Year.ToString(CultureInfo.InvariantCulture) ?? "null");

    public BigInteger Square(BigInteger value) => value * value;

    public string Required(string text, Counter counter) => text + counter.Value.ToString(CultureInfo.InvariantCulture);

    public Counter? OptionalCounter(Counter? counter) => counter;

    public int[] Array() => [1, 2, 3];

    public List<string> List() => ["a", "b"];

    public Dictionary<string, int> Dictionary() => new(StringComparer.Ordinal) { ["a"] = 1 };

    public char Char() => 'x';

    public void Set(string value) => LastValue = value;

    public string WithDefault(string text = "x") => text + Name;

    public Task CompleteAsync() => Task.CompletedTask;

    public ValueTask<string> ValueAsync() => ValueTask.FromResult("value");

    public void Throw() => throw new InvalidOperationException("boom");

    [JsName("class")]
    public string @Class() => "class";
}

public class ExportedHidingBase
{
    public string Hidden() => "base";

    public string Shadowed() => "base";
}

[JsExport]
public sealed partial class ExportedHiding : ExportedHidingBase
{
    [JsIgnore]
    public new string Hidden() => "derived";

    internal new string Shadowed() => "derived";
}

public class ExportedRenamingBase
{
    public virtual string Describe() => "base";

    [JsName("describeWith")]
    public string Describe(string prefix) => prefix + ":" + Describe();

    public virtual int Value { get; set; }

    public virtual int Stored { get; set; }
}

[JsExport]
public sealed partial class ExportedRenaming : ExportedRenamingBase
{
    [JsName("renamed")]
    public override string Describe() => "derived";

    [JsName("renamedValue")]
    public override int Value => base.Value;

    [JsName("renamedStored")]
    public override int Stored { set => base.Stored = value * 2; }
}

[JsExport]
public partial class ExportedSelfPublishing
{
    public ExportedSelfPublishing(JsRealm realm)
    {
        var obj = realm.FromDotNet(this).AsObject();
        obj.SetProperty("mark", 1);
        realm.GlobalObject.SetProperty("during", obj);
    }
}

[JsExport]
public sealed partial class ExportedNotConstructible
{
    private ExportedNotConstructible()
    {
    }

    public static ExportedNotConstructible Create() => new();

    public string Hello() => "hello";
}

public static partial class ExportedContainer
{
    [JsExport]
    internal sealed partial class Nested
    {
        public string Hello() => "nested";
    }
}
