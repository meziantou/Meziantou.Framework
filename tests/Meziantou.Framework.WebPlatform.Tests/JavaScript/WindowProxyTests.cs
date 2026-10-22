using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The WindowProxy (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-windowproxy-exotic-object).</summary>
public sealed class WindowProxyTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, Func<Url, string> loader)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            DocumentLoader = new FuncLoader(loader),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    [Fact]
    public void Window_IsTheSameObjectAfterANavigation()
    {
        var document = Load(
            """
            <iframe src="https://example.com/a"></iframe>
            <script>
              var log = [];
              window.onload = () => {
                const frame = document.querySelector('iframe');
                const before = frame.contentWindow;
                log.push(before === frames[0]);
                frame.onload = () => {
                  log.push(frame.contentWindow === before, before.location.pathname, frames[0].parent === window);
                  document.title = log.join();
                };
                frame.src = 'https://example.com/b';
              };
            </script>
            """,
            _ => "<p>frame</p>");

        Assert.Equal("true,true,/b,true", document.Title);
    }

    [Fact]
    public void WindowProxy_TheTrapsRunInTheRealmOfTheCaller()
    {
        // The WindowProxy of the frame is created by the realm of its child frame (parent), and its traps run in the realm of
        // the caller: the top window is the global object for the code of the top window, not its WindowProxy
        var document = Load(
            """
            <iframe src="https://example.com/a"></iframe>
            <script>
              window.onload = () => {
                document.title = [frames[0].top === window, frames[0].parent === window, frames[0].frames[0].top === window].join();
              };
            </script>
            """,
            url => url.Pathname == "/a" ? "<iframe src='https://example.com/c'></iframe>" : "<script>parent.created = true;</script>");

        Assert.Equal("true,true,true", document.Title);
    }

    [Fact]
    public void CrossOriginWindow_ExposesOnlyTheCrossOriginProperties()
    {
        var document = Load(
            """
            <iframe src="https://other.example/"></iframe>
            <script>
              window.onload = () => {
                const win = frames[0];
                const result = [];
                try { win.document; result.push('document'); } catch (e) { result.push(e.name); }
                result.push(Object.getPrototypeOf(win) === null, win.parent === window, typeof win.close);
                try { win.foo = 1; result.push('set'); } catch (e) { result.push(e.name); }
                document.title = result.join();
              };
            </script>
            """,
            _ => "<p>frame</p>");

        Assert.Equal("SecurityError,true,true,function,SecurityError", document.Title);
    }

    [Fact]
    public void ThisValueOfTheScripts_IsTheWindow()
    {
        var document = Load(
            """
            <script>
              function getThis() { return this; }
              setTimeout(function () { document.title = [this === window, getThis() === window, globalThis === self].join(); }, 0);
            </script>
            """,
            _ => "");

        Assert.Equal("true,true,true", document.Title);
    }

    [Fact]
    public void Window_OfARemovedIframe_KeepsItsWindowProxy()
    {
        var document = Load(
            """
            <body><iframe></iframe>
            <script>
              const frame = document.querySelector('iframe');
              const other = frame.contentWindow;
              const before = [other.self === other, other.globalThis === other, other.frames === other, other.closed];
              frame.remove();
              document.title = before.concat(other.self === other, other.window === other, other.globalThis === other, other.closed).join();
            </script>
            """,
            _ => "");

        Assert.Equal("true,true,true,false,true,true,true,true", document.Title);
    }

    [Fact]
    public void NamedProperties_SeveralElements_AreALiveCollection()
    {
        var document = Load(
            """
            <body><div id="dupe"></div><img name="dupe"><span id="single"></span>
            <script>
              const collection = window.dupe;
              const log = [collection instanceof HTMLCollection, collection.length, window.single === document.querySelector('span')];
              document.querySelector('img').remove();
              log.push(collection.length, window.dupe === document.querySelector('div'));
              document.body.appendChild(document.createElement('p')).id = 'dupe';
              log.push(collection.length, window.dupe.length);
              document.title = log.join();
            </script>
            """,
            _ => "");

        Assert.Equal("true,2,true,1,true,2,2", document.Title);
    }

    [Fact]
    public void NamedProperties_AreExposedByTheNamedPropertiesObject_UnlessItsPrototypeChainShadowsThem()
    {
        var document = Load(
            """
            <body><span id="onWindow"></span><span id="onWindowPrototype"></span><span id="onEventTarget"></span>
            <script>
              window.onWindow = 'shadowing';
              Window.prototype.onWindowPrototype = 'shadowing';
              EventTarget.prototype.onEventTarget = 'shadowing';
              const namedProperties = Object.getPrototypeOf(Window.prototype);
              document.title = [
                window.onWindow, namedProperties.onWindow === document.getElementById('onWindow'),
                window.onWindowPrototype, namedProperties.onWindowPrototype === document.getElementById('onWindowPrototype'),
                window.onEventTarget, namedProperties.onEventTarget, Object.getOwnPropertyDescriptor(namedProperties, 'onEventTarget')].join();
            </script>
            """,
            _ => "");

        Assert.Equal("shadowing,true,shadowing,true,shadowing,shadowing,", document.Title);
    }

    [Fact]
    public void NamedProperties_ACrossOriginChildHidesTheNextChildWithTheSameName()
    {
        var document = Load(
            """
            <body><iframe name="a" src="https://other.example/"></iframe><iframe name="a"></iframe><iframe name="b"></iframe>
            <script>
              window.onload = () => document.title = [typeof window.a, window.b === frames[2]].join();
            </script>
            """,
            _ => "");

        Assert.Equal("undefined,true", document.Title);
    }


    private sealed class FuncLoader(Func<Url, string> loader) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(new DocumentResource(loader(new Url(request.Url))));
    }
}
