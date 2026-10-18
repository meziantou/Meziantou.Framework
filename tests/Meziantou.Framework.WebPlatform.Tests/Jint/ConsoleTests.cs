using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>The console namespace (https://console.spec.whatwg.org/) and the messages given to <see cref="BrowsingContextOptions.ConsoleMessage"/>.</summary>
public sealed class ConsoleTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<ScriptError> _errors = [];
    private readonly List<ConsoleMessage> _messages = [];
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Run(string script, bool handleMessages = true)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            TimeProvider = _clock,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            ConsoleMessage = handleMessages ? (_, message) => _messages.Add(message) : null,
        });

        var document = context.Navigate("https://example.com/", _ => "<script>" + script + "</script>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    private string[] Texts => [.. _messages.Select(m => m.Text)];

    [Fact]
    public void Log_FormatsTheArguments()
    {
        Run("""
            console.log('a', 1, true, null, undefined, 2n, Symbol('s'));
            console.log('%s=%d %i %f%%', 'x', '42.5px', 3.9, '1.5e1', 'rest');
            console.log('%d %i %f', Symbol(), Symbol(), Symbol());
            console.log('%c styled', 'color: red');
            console.log('%o', { a: [1, 'b'], c: { d: { e: 1 } } });
            console.log([1, 2], { x: 'y' });
            console.log('%s', '%d', 5);
            """);
        Assert.Equal([
            "a 1 true null undefined 2n Symbol(s)",
            "x=42 3 15%% rest",
            "NaN NaN NaN",
            " styled",
            "{a: [1, 'b'], c: {d: {…}}}",
            "[1, 2] {x: 'y'}",
            "%d 5",
        ], Texts);
        Assert.All(_messages, m => Assert.Equal("log", m.Method));
        Assert.Equal(["%d %i %f", null, null, null], _messages[2].Arguments.Select(a => a as string));
    }

    [Fact]
    public void Levels()
    {
        Run("console.debug('d'); console.info('i'); console.warn('w'); console.error('e'); console.assert(false); console.assert(true, 'not printed');");
        Assert.Equal([ConsoleMessageLevel.Debug, ConsoleMessageLevel.Info, ConsoleMessageLevel.Warning, ConsoleMessageLevel.Error, ConsoleMessageLevel.Error], _messages.Select(m => m.Level));
        Assert.Equal(["debug", "info", "warn", "error", "assert"], _messages.Select(m => m.Method));
    }

    [Fact]
    public void Assert_PrefixesTheMessage()
    {
        Run("console.assert(false); console.assert(0, 'value: %d', 5); console.assert(false, { a: 1 });");
        Assert.Equal(["Assertion failed", "Assertion failed: value: 5", "Assertion failed {a: 1}"], Texts);
    }

    [Fact]
    public void Count_And_CountReset()
    {
        Run("console.count(); console.count(undefined); console.count('a'); console.countReset(); console.count('default'); console.countReset('missing');");
        Assert.Equal(["default: 1", "default: 2", "a: 1", "default: 1", "Count for 'missing' does not exist"], Texts);
        Assert.Equal(ConsoleMessageLevel.Warning, _messages[^1].Level);
    }

    [Fact]
    public void Timers_UseTheClockOfTheEventLoop()
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            TimeProvider = _clock,
            ConsoleMessage = (_, message) => _messages.Add(message),
        });
        context.Navigate("https://example.com/", _ => "<script>console.time('t'); console.time('t'); setTimeout(() => { console.timeLog('t', 'extra', 1); console.timeEnd('t'); console.timeEnd('t'); }, 1500)</script>");
        _contexts.EventLoop.RunUntilIdle();
        _clock.Advance(TimeSpan.FromMilliseconds(1500.25));
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["Timer 't' already exists", "t: 1500.25 ms extra 1", "t: 1500.25 ms", "Timer 't' does not exist"], Texts);
        Assert.Equal(["time", "timeLog", "timeEnd", "timeEnd"], _messages.Select(m => m.Method));
    }

    [Fact]
    public void Groups_SetTheDepthOfTheMessages()
    {
        Run("console.group('g', 1); console.log('a'); console.groupCollapsed(); console.log('b'); console.groupEnd(); console.groupEnd(); console.groupEnd(); console.log('c'); console.group(); console.clear(); console.log('d');");
        Assert.Equal([("g 1", 0), ("a", 1), ("console.groupCollapsed", 1), ("b", 2), ("c", 0), ("console.group", 0), ("", 0), ("d", 0)], _messages.Select(m => (m.Text, m.GroupDepth)));
        Assert.Equal("clear", _messages[6].Method);
    }

    [Fact]
    public void Trace_HasTheStack()
    {
        Run("function f() { console.trace('%i', '12'); } f();");
        var message = Assert.Single(_messages);
        Assert.Equal("trace", message.Method);
        Assert.Equal("12", message.Text);
        Assert.Contains("f", message.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void Dir_And_Dirxml()
    {
        Run("const p = document.createElement('p'); p.id = 'x'; p.className = 'a b'; p.textContent = 't'; console.dir('s'); console.dir(p); console.dirxml(p, 1); console.log(window, new URLSearchParams(), [document]);");
        Assert.Equal(["s", "p#x.a.b", "<p id=\"x\" class=\"a b\">t</p> 1", "Window URLSearchParams [#document]"], Texts);
    }

    [Fact]
    public void LargeArrays_AreTruncated()
    {
        Run("console.log(new Array(100000).fill('x')); console.log(new Uint8Array(1000));");
        Assert.StartsWith("['x', 'x', ", _messages[0].Text, StringComparison.Ordinal);
        Assert.EndsWith(", … 99900 more]", _messages[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Uint8Array(1000) [0, 0, ", _messages[1].Text, StringComparison.Ordinal);
        Assert.EndsWith(", … 900 more]", _messages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CyclicObjects()
    {
        Run("const a = { name: 'a' }; a.self = a; const b = [1]; b.push(b); console.log(a, b);");
        Assert.Equal(["{name: 'a', self: [Circular]} [1, [Circular]]"], Texts);
    }

    [Fact]
    public void LabelsAreConvertedWithoutHandler()
    {
        var document = Run("""
            const results = [];
            for (const method of ['count', 'countReset', 'time', 'timeLog', 'timeEnd']) {
              try { console[method]({ toString() { throw new Error(method); } }); } catch (e) { results.push(e.message); }
            }
            console.log({ toString() { throw new Error(); } });
            document.title = results.join();
            """, handleMessages: false);
        Assert.Equal("count,countReset,time,timeLog,timeEnd", document.Title);
        Assert.Empty(_messages);
    }

    [Fact]
    public void Namespace()
    {
        var document = Run("""
            const d = Object.getOwnPropertyDescriptor(window, 'console');
            const proto = Object.getPrototypeOf(console);
            const log = Object.getOwnPropertyDescriptor(console, 'log');
            document.title = [d.writable, d.enumerable, d.configurable, Object.getOwnPropertyNames(proto).length, Object.getPrototypeOf(proto) === Object.prototype,
              String(console), log.enumerable, console.log.length, console.log.name, 'Console' in window].join();
            """);
        Assert.Equal("true,false,true,0,true,[object console],true,0,log,false", document.Title);
    }
}
