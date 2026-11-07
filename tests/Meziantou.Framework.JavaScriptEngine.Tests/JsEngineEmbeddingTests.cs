using System.Diagnostics;
using System.Globalization;

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
