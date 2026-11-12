namespace Meziantou.Framework.JavaScriptEngine.AotTests;

/// <summary>
/// Exercises the main features of the engine in a Native AOT, trimmed application: the features that only break once
/// trimmed (reflection, missing metadata, embedded resources removed) fail the tests.
/// </summary>
public sealed class JavaScriptEngineAotTests
{
    [Fact]
    public void ExecuteScriptWithHostFunction()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", 2, (thisValue, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
        }

        Assert.Equal("11-12-13", realm.Execute("[1, 2, 3].map(x => add(x, 10)).join('-')").AsString());

        var function = realm.Execute("(function (a, b) { return `${a}:${b}`; })");
        Assert.Equal("1:x", engine.Call(function, JsValue.Undefined, 1, "x").AsString());
    }

    [Fact]
    public void LanguageFeatures()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        var realm = engine.CreateRealm();
        var result = realm.Execute("""
            class Point {
              #x; #y;
              constructor(x, y) { this.#x = x; this.#y = y; }
              get length() { return Math.hypot(this.#x, this.#y); }
              *[Symbol.iterator]() { yield this.#x; yield this.#y; }
            }
            const p = new Point(3, 4);
            const { a = 1, ...rest } = { b: 2, c: 3 };
            const map = new Map([[1, 'one']]);
            const proxy = new Proxy({}, { get: (_, key) => `proxy:${String(key)}` });
            [p.length, [...p].join('+'), a, Object.keys(rest).join(''), map.get(1), proxy.name, 2n ** 64n, eval('6 * 7')].join('|')
            """);
        Assert.Equal("5|3+4|1|bc|one|proxy:name|18446744073709551616|42", result.AsString());
    }

    [Fact]
    public void JsonAndRegExp()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        var realm = engine.CreateRealm();
        Assert.Equal("""{"a":[1,"b",null],"c":{"d":true}}""", realm.Execute("""JSON.stringify(JSON.parse('{"a":[1,"b",null],"c":{"d":true}}'))""").AsString());
        Assert.Equal("2026|10|23", realm.Execute("""/(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})/u.exec('on 2026-10-23').slice(1).join('|')""").AsString());
        Assert.Equal("Ab-Cd", realm.Execute("""'ab-cd'.replace(/\b\p{Ll}/gu, c => c.toUpperCase())""").AsString());
    }

    [Fact]
    public void PromisesAndAsyncFunctions()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        var realm = engine.CreateRealm();
        realm.Execute("""
            globalThis.log = [];
            (async () => {
              log.push(await Promise.resolve('a'));
              for await (const value of (async function* () { yield 'b'; yield 'c'; })()) log.push(value);
            })();
            log.push('sync');
            """);
        engine.RunJobs();
        Assert.Equal("sync,a,b,c", realm.Execute("log.join()").AsString());
    }

    [Fact]
    public void Exceptions()
    {
        using var engine = new JsEngine(new JsEngineOptions { MaxStatements = 10_000 });
        var realm = engine.CreateRealm();

        var exception = Assert.Throws<JsException>(() => realm.Execute("null.x"));
        Assert.StartsWith("TypeError", exception.Message, StringComparison.Ordinal);

        exception = Assert.Throws<JsException>(() => realm.Execute("let ="));
        Assert.StartsWith("SyntaxError", exception.Message, StringComparison.Ordinal);

        Assert.Throws<JsTerminationException>(() => realm.Execute("while (true) {}"));
    }

    // Intl and Temporal read the CLDR and time zone data embedded in the assembly
    [Fact]
    public void IntlAndTemporal()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10), TimeZone = "UTC" });
        var realm = engine.CreateRealm();
        Assert.Equal("1.234.567,89", realm.Execute("new Intl.NumberFormat('de-DE').format(1234567.89)").AsString());
        Assert.Equal("a,ä,z", realm.Execute("['z', 'ä', 'a'].sort(new Intl.Collator('de').compare).join()").AsString());
        Assert.Equal("Monday, January 2, 2006", realm.Execute("new Intl.DateTimeFormat('en-US', { dateStyle: 'full', timeZone: 'UTC' }).format(new Date(Date.UTC(2006, 0, 2, 15, 4, 5)))").AsString());
        Assert.Equal("2026-03-29T03:30:00+02:00[Europe/Paris]", realm.Execute("Temporal.Instant.from('2026-03-29T01:30:00Z').toZonedDateTimeISO('Europe/Paris').toString()").AsString());
        Assert.Equal("東京|に|住む", realm.Execute("[...new Intl.Segmenter('ja', { granularity: 'word' }).segment('東京に住む')].map(s => s.segment).join('|')").AsString());
    }

    [Fact]
    public void Modules()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10), ModuleLoader = new SingleModuleLoader("math.js", "export const square = x => x * x;") });
        var realm = engine.CreateRealm();
        var module = realm.ParseModule("import { square } from 'math.js'; globalThis.result = square(7);", "main.js");
        module.LoadRequestedModules();
        engine.RunJobs();
        module.Link();
        module.Evaluate();
        engine.RunJobs();

        Assert.Equal(49, realm.Execute("result").AsNumber());
    }

    // The conversions between .NET and JavaScript values test the types: they work without reflection
    [Fact]
    public void Marshalling()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            var order = new Dictionary<string, object?> { ["id"] = "A-12", ["lines"] = new List<object?> { 1, 2.5, null }, ["date"] = DateTimeOffset.UnixEpoch };
            realm.GlobalObject.SetProperty("order", realm.FromDotNet(order));
            realm.GlobalObject.SetProperty("json", realm.FromDotNet(System.Text.Json.JsonDocument.Parse("""{ "a": [true, "x"] }""").RootElement));
            realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", (int a, long? b) => a + (b ?? 1)));
            realm.GlobalObject.SetProperty(JsSymbol.Create("hidden"), 1);
        }

        Assert.Equal("A-12|1,2.5,|1970|true,x|8|5", realm.Execute("[order.id, order.lines.join(), order.date.getUTCFullYear(), json.a.join(), add(3, 5), add('4')].join('|')").AsString());

        var result = (Dictionary<string, object?>)engine.ToDotNet(realm.Execute("({ a: [1, 'b'], c: { d: 10n } })"))!;
        Assert.Equal("b", ((List<object?>)result["a"]!)[1]);
        Assert.Equal(new System.Numerics.BigInteger(10), ((Dictionary<string, object?>)result["c"]!)["d"]);
        Assert.Equal(42, engine.ToDotNet<int>(realm.Execute("'42'")));
    }

    [Fact]
    public async Task TasksAndPromises()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("delay", realm.CreateFunction("delay", 1, (_, arguments) => realm.CreatePromise(Task.Delay(10).ContinueWith(_ => 7, TaskScheduler.Default), value => value)));
        }

        var result = await engine.RunUntilSettledAsync(realm.Execute("(async () => (await delay()) * 6)()"));
        Assert.Equal(42, result.AsNumber());
    }

    // The bindings generated for [JsExport] use no reflection
    [Fact]
    public async Task ExportedClasses()
    {
        using var engine = new JsEngine(new JsEngineOptions { Timeout = TimeSpan.FromSeconds(10) });
        var realm = engine.CreateRealm();
        var account = new AotAccount("A-1");
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("Account", realm.GetClass(AotAccount.JsClass));
            realm.GlobalObject.SetProperty("existing", realm.FromDotNet(account));
        }

        Assert.Equal("B-2|15|A-1|true|TypeError", realm.Execute("""
            const account = new Account('B-2');
            account.deposit(10);
            account.deposit(5);
            class Savings extends Account { }
            [account.id, account.balance, existing.id, new Savings('C').transfer(existing, 0) === existing, (() => { try { Account(); } catch (e) { return e.name; } })()].join('|')
            """).AsString());
        Assert.Equal(7, (await engine.RunUntilSettledAsync(realm.Execute("existing.depositLaterAsync(7)"))).AsNumber());
        Assert.Equal(7, account.Balance);
        Assert.Same(account, engine.ToDotNet<AotAccount>(realm.Execute("existing")));
    }

    private sealed class SingleModuleLoader(string specifier, string code) : JsModuleLoader
    {
        protected override void LoadImportedModule(JsModuleLoadRequest request)
        {
            if (request.Specifier == specifier)
            {
                request.Complete(request.Realm.ParseModule(code, specifier));
            }
            else
            {
                request.Fail("Cannot find the module '" + request.Specifier + "'");
            }
        }
    }
}

[JsExport]
public sealed partial class AotAccount(string id)
{
    public string Id { get; } = id;

    public double Balance { get; private set; }

    public double Deposit(double amount) => Balance += amount;

    public AotAccount Transfer(AotAccount target, double amount)
    {
        Balance -= amount;
        target.Deposit(amount);
        return target;
    }

    public async Task<double> DepositLaterAsync(double amount)
    {
        await Task.Delay(1);
        return Deposit(amount);
    }
}
