namespace Meziantou.Framework.JavaScriptEngine.WebApis.Tests;

public sealed class ConsoleTests
{
    [Fact]
    public void Console_ConsoleMessage()
    {
        #region JsWebApis_ConsoleMessage
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var messages = new List<JsConsoleMessage>();
        JsWebApis.Install(realm, new JsWebApisOptions { ConsoleMessage = messages.Add });

        realm.Execute("""
            console.warn('%s has %d items', 'cart', 3, { total: 12.5 });
            """);

        var message = Assert.Single(messages);
        Assert.Equal("warn", message.Method);
        Assert.Equal(JsConsoleMessageLevel.Warning, message.Level);
        Assert.Equal("cart has 3 items { total: 12.5 }", message.Text);
        #endregion
    }

    [Fact]
    public void Console_Levels()
    {
        using var context = new WebApisTestContext();
        context.Run("console.log('l'); console.info('i'); console.warn('w'); console.error('e'); console.debug('d');");
        Assert.Equal(
            [JsConsoleMessageLevel.Log, JsConsoleMessageLevel.Info, JsConsoleMessageLevel.Warning, JsConsoleMessageLevel.Error, JsConsoleMessageLevel.Debug],
            context.Messages.Select(message => message.Level));
        Assert.Equal(["log", "info", "warn", "error", "debug"], context.Messages.Select(message => message.Method));
    }

    [Theory]
    [InlineData("'a', 1, true, null, undefined", "a 1 true null undefined")]
    [InlineData("'%s is %d years and %f%%', 'Bob', '42.5', '1.5'", "Bob is 42 years and 1.5%")]
    [InlineData("'%i|%d|%f', 4.9, Symbol(), 'x'", "4|NaN|NaN")]
    [InlineData("'%cred', 'color: red'", "red")]
    [InlineData("'%s', 1n", "1n")]
    [InlineData("'%o', 'text'", "'text'")]
    [InlineData("'%s %s', 'one'", "one %s")]
    [InlineData("'100%', 'extra'", "100% extra")]
    [InlineData("'%x', 1", "%x 1")]
    [InlineData("{ a: 1 }, 'b'", "{ a: 1 } b")]
    [InlineData("", "")]
    public void Console_Format(string arguments, string expected)
    {
        using var context = new WebApisTestContext();
        context.Run($"console.log({arguments})");
        Assert.Equal([expected], context.MessageTexts);
    }

    [Theory]
    [InlineData("{ a: 1, 'b-c': 's', [Symbol('k')]: [1, 'x'] }", "{ a: 1, 'b-c': 's', [Symbol(k)]: [ 1, 'x' ] }")]
    [InlineData("{}", "{}")]
    [InlineData("[]", "[]")]
    [InlineData("[1, , 3]", "[ 1, <empty item>, 3 ]")]
    [InlineData("{ a: { b: { c: { d: 1 } } } }", "{ a: { b: { c: [Object] } } }")]
    [InlineData("[[[[1]]]]", "[ [ [ [Array] ] ] ]")]
    [InlineData("new Map([[1, 'a']])", "Map(1) { 1 => 'a' }")]
    [InlineData("new Set([1, 2])", "Set(2) { 1, 2 }")]
    [InlineData("new Date(0)", "1970-01-01T00:00:00.000Z")]
    [InlineData("new Date(NaN)", "Invalid Date")]
    [InlineData("/a+/g", "/a+/g")]
    [InlineData("new Uint8Array([1, 2])", "Uint8Array(2) [ 1, 2 ]")]
    [InlineData("new ArrayBuffer(3)", "ArrayBuffer { byteLength: 3 }")]
    [InlineData("function f() {}", "[Function: f]")]
    [InlineData("() => {}", "[Function (anonymous)]")]
    [InlineData("class A {}", "[class A]")]
    [InlineData("new (class Point { constructor() { this.x = 1; } })()", "Point { x: 1 }")]
    [InlineData("Object.create(null)", "[Object: null prototype] {}")]
    [InlineData("{ get a() { return 1; }, set b(v) {} }", "{ a: [Getter], b: [Setter] }")]
    [InlineData("-0", "-0")]
    [InlineData("10n", "10n")]
    [InlineData("Symbol('s')", "Symbol(s)")]
    [InlineData("{ s: \"it's\\n\" }", "{ s: 'it\\'s\\n' }")]
    public void Console_Inspect(string value, string expected)
    {
        using var context = new WebApisTestContext();
        context.Run($"console.log(({value}))");
        Assert.Equal([expected], context.MessageTexts);
    }

    [Fact]
    public void Console_InspectCircularAndErrors()
    {
        using var context = new WebApisTestContext();
        context.Run("const o = { a: 1 }; o.self = o; console.log(o); console.log(new Error('boom'));");
        Assert.Equal("{ a: 1, self: [Circular] }", context.Messages[0].Text);
        Assert.StartsWith("Error: boom", context.Messages[1].Text);
    }

    [Fact]
    public void Console_InspectDoesNotCallGettersOrProxies()
    {
        using var context = new WebApisTestContext();
        context.Run("""
            const proxy = new Proxy({}, { ownKeys() { throw new Error('trap'); } });
            console.log({ get a() { throw new Error('getter'); } }, proxy);
            """);
        Assert.Equal(["{ a: [Getter] } [object]"], context.MessageTexts);
    }

    [Fact]
    public void Console_Assert()
    {
        using var context = new WebApisTestContext();
        context.Run("console.assert(true, 'no'); console.assert(false); console.assert(0, 'x %d', 1); console.assert(false, { a: 1 });");
        Assert.Equal(["Assertion failed", "Assertion failed: x 1", "Assertion failed { a: 1 }"], context.MessageTexts);
        Assert.All(context.Messages, message => Assert.Equal(JsConsoleMessageLevel.Error, message.Level));
    }

    [Fact]
    public void Console_Count()
    {
        using var context = new WebApisTestContext();
        context.Run("console.count(); console.count(); console.count('x'); console.countReset(); console.count(); console.countReset('missing');");
        Assert.Equal(["default: 1", "default: 2", "x: 1", "default: 1", "Count for 'missing' does not exist"], context.MessageTexts);
        Assert.Equal(JsConsoleMessageLevel.Warning, context.Messages[^1].Level);
    }

    [Fact]
    public void Console_Group()
    {
        using var context = new WebApisTestContext();
        context.Run("console.log('a'); console.group('g'); console.log('b'); console.groupCollapsed(); console.log('c'); console.groupEnd(); console.groupEnd(); console.groupEnd(); console.log('d');");
        Assert.Equal(["a", "g", "b", "c", "d"], context.MessageTexts);
        Assert.Equal([0, 0, 1, 2, 0], context.Messages.Select(message => message.GroupDepth));
    }

    [Fact]
    public void Console_Time()
    {
        var timeProvider = new ManualTimeProvider();
        using var context = new WebApisTestContext(timeProvider: timeProvider);
        context.Run("console.time(); console.time();");
        timeProvider.Advance(TimeSpan.FromMilliseconds(1.5));
        context.Run("console.timeLog(undefined, 'step'); console.timeEnd(); console.timeEnd(); console.timeLog('x');");
        Assert.Equal(["Timer 'default' already exists", "default: 1.5ms step", "default: 1.5ms", "Timer 'default' does not exist", "Timer 'x' does not exist"], context.MessageTexts);
    }

    [Fact]
    public void Console_Trace()
    {
        using var context = new WebApisTestContext();
        context.Realm.Execute("function f() { console.trace('here %d', 1); } f();", "test.js");
        var message = Assert.Single(context.Messages);
        Assert.Equal("trace", message.Method);
        Assert.StartsWith("Trace: here 1\n", message.Text);
        Assert.Contains("test.js", message.Text);
        Assert.DoesNotContain(JsWebApis.ScriptUrl, message.Text);
    }

    [Fact]
    public void Console_DirTableAndClear()
    {
        using var context = new WebApisTestContext();
        context.Run("console.dir('s'); console.dir({ a: 1 }); console.table([1]); console.dirxml('x', 1); console.group(); console.clear(); console.log('after');");
        Assert.Equal(["'s'", "{ a: 1 }", "[ 1 ]", "x 1", "after"], context.MessageTexts);
        Assert.Equal(0, context.Messages[^1].GroupDepth);
    }

    [Fact]
    public void Console_Object()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("[object console]", context.Run("Object.prototype.toString.call(console)"));
        Assert.Equal("true", context.Run("Object.getOwnPropertyDescriptor(console, 'log').enumerable"));

        // The methods do not depend on this
        Assert.Equal("undefined", context.Run("const { log } = console; log('detached')"));
        Assert.Equal(["detached"], context.MessageTexts);
    }
}
