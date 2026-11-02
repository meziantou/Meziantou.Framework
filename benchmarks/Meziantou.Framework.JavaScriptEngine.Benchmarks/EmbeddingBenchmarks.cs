using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.JavaScriptEngine.Benchmarks;

/// <summary>The costs of an application that embeds the engine: compiling, running top-level code, sharing prepared scripts.</summary>
[MemoryDiagnoser]
public class EmbeddingBenchmarks
{
    private const int EngineCount = 8;

    private static readonly JsPreparedScript SmallScript = JsPreparedScript.Parse("1 + 1");

    /// <summary>A top-level loop: the variables are properties of the global object.</summary>
    private static readonly JsPreparedScript GlobalLoopScript = JsPreparedScript.Parse("var total = 0; for (var i = 0; i < 10000; i++) { total += i % 7; } total");

    /// <summary>A script that each engine runs: functions, objects and property accesses.</summary>
    private static readonly JsPreparedScript SharedScript = JsPreparedScript.Parse("""
        function area(shape) { return shape.w * shape.h; }
        let s = 0;
        for (let i = 0; i < 2000; i++) { s += area({ w: i, h: 2 }) + area({ h: 3, w: i, kind: 'b' }); }
        s
        """);

    private JsEngine? _engine;
    private JsRealm? _realm;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new JsEngine();
        _realm = _engine.CreateRealm();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
    }

    /// <summary>Parses and compiles the code on each call.</summary>
    [Benchmark(Baseline = true)]
    public double ExecuteString() => _realm!.Execute("1 + 1").AsNumber();

    /// <summary>Runs code compiled once.</summary>
    [Benchmark]
    public double ExecutePrepared() => _realm!.Execute(SmallScript).AsNumber();

    [Benchmark]
    public double TopLevelGlobalLoop() => _realm!.Execute(GlobalLoopScript).AsNumber();

    /// <summary>One prepared script run by several engines, one after the other.</summary>
    [Benchmark]
    public double SharedScriptSequentialEngines()
    {
        var total = 0d;
        for (var i = 0; i < EngineCount; i++)
        {
            using var engine = new JsEngine();
            total += engine.CreateRealm().Execute(SharedScript).AsNumber();
        }

        return total;
    }

    /// <summary>One prepared script run by several engines at the same time, one per thread.</summary>
    [Benchmark]
    public double SharedScriptConcurrentEngines()
    {
        var results = new double[EngineCount];
        Parallel.For(0, EngineCount, i =>
        {
            using var engine = new JsEngine();
            results[i] = engine.CreateRealm().Execute(SharedScript).AsNumber();
        });

        return results.Sum();
    }
}

/// <summary>The first uses of Intl in a new engine, for several locales.</summary>
[MemoryDiagnoser]
public class IntlBenchmarks
{
    private static readonly JsPreparedScript MultiLocaleScript = JsPreparedScript.Parse("""
        const date = new Date(Date.UTC(2026, 9, 29, 12));
        let length = 0;
        for (const locale of ['en-US', 'fr-FR', 'de-DE', 'ja-JP', 'ar-EG', 'hi-IN', 'zh-Hans-CN', 'ru-RU']) {
          length += new Intl.NumberFormat(locale, { style: 'currency', currency: 'EUR' }).format(1234.5).length;
          length += new Intl.DateTimeFormat(locale, { dateStyle: 'full', timeStyle: 'short', timeZone: 'UTC' }).format(date).length;
          length += new Intl.PluralRules(locale).select(2).length;
          length += ['b', 'a', 'c'].sort(new Intl.Collator(locale).compare).join().length;
        }
        length
        """);

    /// <summary>A new engine, so the formatters and the locale data of the engine are created again.</summary>
    [Benchmark]
    public double MultiLocaleColdStart()
    {
        using var engine = new JsEngine();
        return engine.CreateRealm().Execute(MultiLocaleScript).AsNumber();
    }
}
