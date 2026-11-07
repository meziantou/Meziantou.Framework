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
    public void WindowProxy_IsTheSameObjectForEveryRealm()
    {
        // The WindowProxy of the top window may be created by the realm of a frame (parent): it is also the window of the
        // code of the top window
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


    [Fact]
    public void GlobalThis_IsTheWindowProxy()
    {
        var document = Load(
            """
            <script>
              'use strict';
              const log = [this === window, globalThis === window, self === window, window.window === window, frames === window];
              log.push(Object.getPrototypeOf(window) === Window.prototype, Object.isExtensible(window), Reflect.preventExtensions(window));
              try { Object.preventExtensions(window); log.push('prevented'); } catch (e) { log.push(e.name); }
              log.push(Reflect.setPrototypeOf(window, {}), Reflect.setPrototypeOf(window, Window.prototype));
              var declared = 1;
              log.push(window.declared, Object.getOwnPropertyDescriptor(window, 'declared').configurable);
              document.title = log.join();
            </script>
            """,
            _ => "");

        Assert.Equal("true,true,true,true,true,true,true,false,TypeError,false,true,1,false", document.Title);
    }

    [Fact]
    public void IndexedProperties_AreTheWindowsOfTheChildNavigables()
    {
        var document = Load(
            """
            <iframe></iframe>
            <script>
              'use strict';
              const descriptor = Object.getOwnPropertyDescriptor(window, 0);
              const log = [descriptor.value === frames[0], descriptor.writable, descriptor.enumerable, descriptor.configurable, 0 in window, 1 in window];
              log.push(Reflect.defineProperty(window, 0, { value: 1 }), Reflect.defineProperty(window, 1, { value: 1 }), Reflect.set(window, 1, 1), 1 in window);
              try { window[1] = 1; log.push('set'); } catch (e) { log.push(e.name); }
              log.push(Reflect.deleteProperty(window, 0), Reflect.deleteProperty(window, 1), Reflect.ownKeys(window)[0]);
              document.querySelector('iframe').remove();
              log.push(0 in window, window[0]);
              document.title = log.join();
            </script>
            """,
            _ => "");

        Assert.Equal("true,false,true,true,true,false,false,false,false,false,TypeError,false,true,0,false,", document.Title);
    }

    [Fact]
    public void CrossOriginLocation_ExposesOnlyTheCrossOriginProperties()
    {
        var document = Load(
            """
            <iframe src="https://other.example/"></iframe>
            <script>
              window.onload = () => {
                const location = frames[0].location;
                const log = [location === frames[0].location, Object.getPrototypeOf(location), Reflect.preventExtensions(location), Reflect.ownKeys(location).slice(0, 3).join(' ')];
                const href = Object.getOwnPropertyDescriptor(location, 'href');
                log.push(href.get, typeof href.set, href.set.name, Object.getPrototypeOf(href.set) === Function.prototype, String({}.toString.call(location)));
                for (const access of [() => location.href, () => location.pathname, () => location.assign, () => Object.defineProperty(location, 'x', { value: 1 }), () => delete location.replace]) {
                  try { access(); log.push('allowed'); } catch (e) { log.push(e.name); }
                }
                document.title = log.join();
              };
            </script>
            """,
            _ => "<p>frame</p>");

        Assert.Equal("true,,false,href replace then,,function,set href,true,[object Object],SecurityError,SecurityError,SecurityError,SecurityError,SecurityError", document.Title);
    }

    [Fact]
    public void CrossOriginFunctions_AreCreatedPerCallerRealmAndRunTheStepsOfTheIdlMember()
    {
        // The frame replaces its focus method: the cross-origin focus function still performs the steps of Window.focus()
        var document = Load(
            """
            <iframe src="https://other.example/"></iframe>
            <iframe></iframe>
            <script>
              window.onload = () => {
                const win = frames[0];
                const sameOrigin = frames[1];
                const close = win.close;
                const closeFromFrame = sameOrigin.eval('parent.frames[0].close');
                const parentGetter = Object.getOwnPropertyDescriptor(win, 'parent').get;
                document.title = [
                  close === win.close, close !== window.close, close !== closeFromFrame, Object.getPrototypeOf(close) === Function.prototype,
                  Object.getPrototypeOf(closeFromFrame) === sameOrigin.Function.prototype, close.name, win.postMessage.length,
                  typeof win.focus, win.focus.name, parentGetter.name, parentGetter.call(undefined) === window,
                ].join();
              };
            </script>
            """,
            url => url.Host == "other.example" ? "<script>window.focus = 'override';</script>" : "");

        Assert.Equal("true,true,true,true,true,close,1,function,focus,get parent,true", document.Title);
    }

    private sealed class FuncLoader(Func<Url, string> loader) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(new DocumentResource(loader(new Url(request.Url))));
    }
}
