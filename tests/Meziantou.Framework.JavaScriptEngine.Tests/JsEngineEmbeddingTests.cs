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
            CancellationToken = new CancellationToken(canceled: true),
            TimeProvider = new FixedTimeProvider(),
            TimeZone = "Europe/Paris",
            DefaultLocale = "fr-FR",
            ModuleLoader = new NoModuleLoader(),
            Host = new JsHost(),
        };

        using var engine = new JsEngine(options);
        foreach (var property in typeof(JsEngineOptions).GetProperties())
        {
            Assert.Equal(property.GetValue(options), property.GetValue(engine.Options));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoModuleLoader : JsModuleLoader
    {
        protected internal override void LoadImportedModule(JsModuleLoadRequest request) => throw new NotSupportedException();
    }
}
