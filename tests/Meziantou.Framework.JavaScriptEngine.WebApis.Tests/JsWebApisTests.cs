namespace Meziantou.Framework.JavaScriptEngine.WebApis.Tests;

public sealed class JsWebApisTests
{
    [Fact]
    public async Task Install()
    {
        #region JsWebApis_Install
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var messages = new List<string>();
        var webApis = JsWebApis.Install(realm, new JsWebApisOptions
        {
            ConsoleMessage = message => messages.Add(message.Text),
        });

        realm.Execute("""
            const url = new URL('/docs?page=2', 'https://example.com');
            setTimeout(() => console.log(url.searchParams.get('page'), btoa('hi')), 10);
            """);

        // Runs the jobs and the timers until there is nothing left to run
        await webApis.RunUntilIdleAsync();
        Assert.Equal(["2 aGk="], messages);
        #endregion
    }

    [Fact]
    public void Install_Options()
    {
        #region JsWebApis_Options
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        JsWebApis.Install(realm, new JsWebApisOptions
        {
            Timers = false,
            Console = false,
        });

        var types = realm.Execute("[typeof setTimeout, typeof console, typeof URL].join()").AsString();
        Assert.Equal("undefined,undefined,function", types);
        #endregion
    }

    [Fact]
    public void Install_NoApi()
    {
        using var context = new WebApisTestContext(new JsWebApisOptions
        {
            Console = false,
            Timers = false,
            QueueMicrotask = false,
            StructuredClone = false,
            Url = false,
            Encoding = false,
            Base64 = false,
            DomException = false,
        });

        var names = new[] { "console", "setTimeout", "setInterval", "clearTimeout", "clearInterval", "queueMicrotask", "structuredClone", "URL", "URLSearchParams", "TextEncoder", "TextDecoder", "atob", "btoa", "DOMException" };
        foreach (var name in names)
        {
            Assert.Equal("undefined", context.Run($"typeof {name}"));
        }
    }

    [Fact]
    public void Install_GlobalPropertyAttributes()
    {
        using var context = new WebApisTestContext();

        // The interfaces and the namespaces are not enumerable, the operations are
        Assert.Equal("true,false,true", context.Run("const d = Object.getOwnPropertyDescriptor(globalThis, 'URL'); [d.writable, d.enumerable, d.configurable].join()"));
        Assert.Equal("false", context.Run("Object.getOwnPropertyDescriptor(globalThis, 'console').enumerable"));
        Assert.Equal("true", context.Run("Object.getOwnPropertyDescriptor(globalThis, 'atob').enumerable"));
        Assert.Equal("true", context.Run("Object.getOwnPropertyDescriptor(URL.prototype, 'href').enumerable"));
        Assert.Equal("[object URL]", context.Run("Object.prototype.toString.call(new URL('https://example.com'))"));
    }

    [Fact]
    public void Install_DomExceptionDisabled_ApisStillThrowDomExceptions()
    {
        using var context = new WebApisTestContext(new JsWebApisOptions { DomException = false });
        Assert.Equal("undefined", context.Run("typeof DOMException"));
        Assert.Equal("InvalidCharacterError", context.GetErrorName("atob('a')"));
    }

    [Fact]
    public void Install_InstalledApisDoNotUseTheModifiedBuiltIns()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("a=1&b=2", context.Run("""
            Array.prototype.push = () => { throw new Error('push'); };
            Map.prototype.get = () => { throw new Error('get'); };
            const params = new URLSearchParams([['a', '1'], ['b', '2']]);
            params.toString()
            """));
    }

    [Fact]
    public void QueueMicrotask()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("0,1,2,3", context.Run("""
            const log = [];
            queueMicrotask(() => log.push(1));
            Promise.resolve().then(() => log.push(2)).then(() => log.push(3));
            log.push(0);
            log
            """));
        Assert.Equal("0,1,2,3", context.Run("log.join()"));
        Assert.Equal("TypeError", context.GetErrorName("queueMicrotask('code')"));
        Assert.Equal("TypeError", context.GetErrorName("queueMicrotask()"));
    }

    [Fact]
    public void QueueMicrotask_ExceptionIsReported()
    {
        using var context = new WebApisTestContext();
        context.Realm.Execute("queueMicrotask(() => { throw new RangeError('boom'); })");
        var exception = Assert.Throws<JsException>(context.Engine.RunJobs);
        Assert.Contains("boom", exception.Message);
    }

    [Fact]
    public void Base64()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("aGVsbG8=", context.Run("btoa('hello')"));
        Assert.Equal("/w==", context.Run("btoa('\\xff')"));
        Assert.Equal("hello", context.Run("atob('aGVsbG8=')"));
        Assert.Equal("hello", context.Run("atob(' aGVs\\nbG8 ')"));
        Assert.Equal("hello", context.Run("atob('aGVsbG8')"));
        Assert.Equal("", context.Run("atob('')"));
        Assert.Equal("158,233,101", context.Run("[...atob(null)].map(c => c.charCodeAt(0)).join()"));
        Assert.Equal("255", context.Run("atob('/w==').charCodeAt(0)"));
        Assert.Equal("InvalidCharacterError", context.GetErrorName("atob('a')"));
        Assert.Equal("InvalidCharacterError", context.GetErrorName("atob('aGVsbG8==')"));
        Assert.Equal("InvalidCharacterError", context.GetErrorName("atob('a*==')"));
        Assert.Equal("InvalidCharacterError", context.GetErrorName("btoa('€')"));
        Assert.Equal("TypeError", context.GetErrorName("btoa()"));
        Assert.Equal("TypeError", context.GetErrorName("btoa(Symbol())"));
        Assert.Equal("5,true", context.Run("try { atob('a'); } catch (e) { [e.code, e instanceof DOMException].join() }"));
    }

    [Fact]
    public void DomException()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("NotFoundError,m,8", context.Run("const e = new DOMException('m', 'NotFoundError'); [e.name, e.message, e.code].join()"));
        Assert.Equal("Error,,0", context.Run("const e2 = new DOMException(); [e2.name, e2.message, e2.code].join()"));
        Assert.Equal("true,true", context.Run("[e instanceof DOMException, e instanceof Error].join()"));
        Assert.Equal("[object DOMException]", context.Run("Object.prototype.toString.call(e)"));
        Assert.Equal("NotFoundError: m", context.Run("String(e)"));
        Assert.Equal("true", context.Run("e.stack.startsWith('NotFoundError: m')"));
        Assert.Equal("1,25,25", context.Run("[DOMException.INDEX_SIZE_ERR, DOMException.DATA_CLONE_ERR, e.DATA_CLONE_ERR].join()"));
        Assert.Equal("false", context.Run("Object.hasOwn(e, 'message')"));
        Assert.Equal("true", context.Run("Object.getPrototypeOf(DOMException.prototype) === Error.prototype"));
        Assert.Equal("TypeError", context.GetErrorName("Object.getOwnPropertyDescriptor(DOMException.prototype, 'name').get.call(new Error())"));
        Assert.Equal("TypeError", context.GetErrorName("DOMException()"));
    }
}
