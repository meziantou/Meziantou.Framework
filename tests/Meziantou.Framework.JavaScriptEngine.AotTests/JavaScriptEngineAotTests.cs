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
