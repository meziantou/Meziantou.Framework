using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>End-to-end tests of the script engine of Meziantou.Framework.JavaScriptEngine: the scripts of a document run and use the DOM.</summary>
public sealed class JavaScriptScriptEngineTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<ScriptError> _errors = [];
    private readonly JavaScriptScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html, JavaScriptScriptEngine? engine = null, DocumentLoader? loader = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = engine ?? _engine,
            DocumentLoader = loader,
            TimeProvider = _clock,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public async Task ReadmeExample_ExecuteScripts()
    {
        #region WebPlatformJavaScript_ExecuteScripts
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
        {
            Timeout = TimeSpan.FromSeconds(5), // per script or callback
        });

        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            ScriptEngine = engine,
            DocumentLoader = DocumentLoader.Create(), // optional: external scripts, modules, fetch, XMLHttpRequest, WebSocket, EventSource...
            UnhandledScriptError = (window, error) => Console.Error.WriteLine(error),
            ConsoleMessage = (window, message) => Console.WriteLine(message.Text), // console.log(), console.count()...
        });

        var document = await context.NavigateAsync("https://example.com/", _ => """
            <title>Example</title>
            <ul id=list></ul>
            <script>
              setTimeout(() => document.getElementById('list').innerHTML = '<li>a<li>b', 100);
            </script>
            """);

        // Run the tasks and the timers until there is nothing left
        do { await context.EventLoop.RunUntilIdleAsync(); } while (await context.EventLoop.WaitForNextTimerAsync());

        Assert.Equal("<li>a</li><li>b</li>", document.GetElementById("list")!.InnerHtml);

        // Evaluate code in the realm of a window
        var title = engine.Evaluate(context.Window, "document.title");
        Assert.Equal("Example", title.AsString());
        #endregion
    }

    [Fact]
    public void RecursionLimit_Null_UnboundedRecursionThrowsARangeError()
    {
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { RecursionLimit = null, MemoryLimit = null });
        var document = Load("<script>try { (function f() { 1 + f(); })(); } catch (e) { document.title = e.constructor.name; }</script>", engine);
        Assert.Equal("RangeError", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Evaluate_RunsCodeInTheRealmOfAWindow()
    {
        var document = Load("<script>var x = 40;</script>");
        Assert.Equal(42, _engine.Evaluate(document.DefaultView!, "x + 2").AsNumber());
        Assert.Empty(_errors);
    }

    [Fact]
    public void Evaluate_PreparedScript_RunsInEachRealm()
    {
        var script = JsPreparedScript.Parse("typeof marker === 'undefined' ? (globalThis.marker = location.href) : 'again'");
        var first = Load("<p>a");
        var second = Load("<p>b");

        Assert.Equal("https://example.com/", _engine.Evaluate(first.DefaultView!, script).AsString());
        Assert.Equal("again", _engine.Evaluate(first.DefaultView!, script).AsString());
        Assert.Equal("https://example.com/", _engine.Evaluate(second.DefaultView!, script).AsString());
    }

    [Fact]
    public void Evaluate_IsNotRestrictedByTheContentSecurityPolicy()
    {
        var document = Load("<meta http-equiv=Content-Security-Policy content=\"script-src 'unsafe-inline'\"><script>try { eval('1'); document.title = 'allowed'; } catch (e) { document.title = e.name; }</script>");
        var function = _engine.Evaluate(document.DefaultView!, "(code) => eval(code)");

        Assert.Equal("EvalError", document.Title);
        Assert.Equal(3, _engine.Evaluate(document.DefaultView!, "eval('1 + 2')").AsNumber());
        Assert.Equal(4, _engine.Call(document.DefaultView!, function, JsValue.Undefined, "2 + 2").AsNumber());
        _errors.Clear();
    }

    [Fact]
    public void ToJsValueAndFromJsValue_ConvertThePlatformObjects()
    {
        var document = Load("<p id=a>text</p>");
        var window = document.DefaultView!;
        var paragraph = document.GetElementById("a")!;

        var wrapper = _engine.ToJsValue(window, paragraph);
        var function = _engine.Evaluate(window, "(e, s) => e.textContent + ':' + s + ':' + (e === document.getElementById('a'))");
        var result = _engine.Engine.Call(function, JsValue.Undefined, [wrapper, _engine.ToJsValue(window, "x")]);

        Assert.Equal("text:x:true", result.AsString());
        Assert.Same(paragraph, _engine.FromJsValue(window, _engine.Evaluate(window, "document.getElementById('a')")));
        Assert.Same(window, _engine.FromJsValue(window, _engine.Evaluate(window, "window")));
        Assert.Equal("s", _engine.FromJsValue(window, _engine.Evaluate(window, "'s'")));
        Assert.Null(_engine.FromJsValue(window, JsValue.Undefined));
        Assert.Null(_engine.FromJsValue(window, JsValue.Null));
    }

    [Fact]
    public void UserPromptHandler_ShowsTheSimpleDialogs()
    {
        var prompts = new List<string>();
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            UserPromptHandler = (window, prompt) =>
            {
                prompts.Add($"{prompt.Type}:{prompt.Message}:{prompt.DefaultValue}");
                return prompt.Type switch
                {
                    UserPromptType.Confirm => UserPromptResult.Accept(),
                    UserPromptType.Prompt when prompt.Message == "name?" => UserPromptResult.Accept("Ada"),
                    UserPromptType.Prompt when prompt.Message == "default?" => UserPromptResult.Accept(),
                    _ => UserPromptResult.Dismiss,
                };
            },
        });

        var document = context.Navigate("https://example.com/", _ => """
            <script>
              alert('hello\r\nworld');
              document.title = [confirm('sure?'), prompt('name?', 'x'), prompt('default?', 'd'), prompt('cancel?')].join();
            </script>
            """);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("true,Ada,d,", document.Title);
        Assert.Equal(["Alert:hello\nworld:", "Confirm:sure?:", "Prompt:name?:x", "Prompt:default?:d", "Prompt:cancel?:"], prompts);
        Assert.Empty(_errors);
    }

    [Fact]
    public void UserPromptHandler_WithoutHandler_ConfirmReturnsFalseAndPromptReturnsNull()
    {
        var document = Load("<script>alert('a'); document.title = [confirm('b'), prompt('c') === null].join();</script>");

        Assert.Equal("false,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void UserPromptHandler_BeforeUnload_CancelsTheNavigationWhenDismissed()
    {
        const string Page = "<style>body { margin: 0 } button { width: 100px; height: 50px }</style><button>x</button><script>addEventListener('beforeunload', e => e.preventDefault());</script>";
        var prompts = new List<UserPromptType>();
        var accept = false;
        var loader = new RecordingLoader();
        loader.Resources["https://example.com/next"] = new DocumentResource("<p>next");
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = loader,
            TimeProvider = _clock,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            UserPromptHandler = (_, prompt) =>
            {
                prompts.Add(prompt.Type);
                return accept ? UserPromptResult.Accept() : UserPromptResult.Dismiss;
            },
        });

        // Without sticky activation, the document cannot prevent the unloading
        context.Navigate("https://example.com/", _ => Page);
        _contexts.EventLoop.RunUntilIdle();
        _engine.Evaluate(context.Window, "location.href = 'next'");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(prompts);
        Assert.Equal("https://example.com/next", context.Document.Url);

        var document = context.Navigate("https://example.com/", _ => Page);
        _contexts.EventLoop.RunUntilIdle();
        context.Input.Mouse.Move(10, 10);
        context.Input.Mouse.Down();
        context.Input.Mouse.Up();

        _engine.Evaluate(context.Window, "location.href = 'next'");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal([UserPromptType.BeforeUnload], prompts);
        Assert.Same(document, context.Document);

        accept = true;
        _engine.Evaluate(context.Window, "location.href = 'next'");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal([UserPromptType.BeforeUnload, UserPromptType.BeforeUnload], prompts);
        Assert.Equal("https://example.com/next", context.Document.Url);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Timeout_StopsTheScript()
    {
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { Timeout = TimeSpan.FromMilliseconds(100) });
        var document = Load("<script>while (true) {}</script><script>document.title = 'next'</script>", engine);
        Assert.Equal("next", document.Title);
        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void MaxStatements_StopsTheScript()
    {
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { MaxStatements = 1000 });
        var document = Load("<script>function f(n) { return n < 2 ? n : f(n - 1) + f(n - 2); } f(40);</script><script>document.title = 'next'</script>", engine);
        Assert.Equal("next", document.Title);
        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void Errors_AreLocatedInTheDocument()
    {
        var document = Load("""
            <script>
              var a = 1;
              null.x;
            </script>
            <script>
              var = 1;
            </script>
            <div onclick="undefinedFunction()"></div>
            <script>document.querySelector('div').click();</script>
            """);
        Assert.Equal(3, _errors.Count);
        Assert.Equal(("TypeError", "https://example.com/", 3), (TestScriptEngines.GetErrorName(_errors[0]), _errors[0].Filename, _errors[0].LineNumber));
        Assert.Equal(("SyntaxError", "https://example.com/", 6), (TestScriptEngines.GetErrorName(_errors[1]), _errors[1].Filename, _errors[1].LineNumber));
        Assert.Equal(("ReferenceError", "https://example.com/"), (TestScriptEngines.GetErrorName(_errors[2]), _errors[2].Filename));
        Assert.NotNull(document.Body);
    }

    [Fact]
    public void Microtasks_RunAfterTheScript()
    {
        var document = Load("""
            <script>
              const log = [];
              Promise.resolve().then(() => log.push('promise'));
              queueMicrotask(() => log.push('microtask'));
              log.push('script');
              setTimeout(() => document.title = log.join(), 0);
            </script>
            """);
        Assert.Equal("script,promise,microtask", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void UnhandledRejections_AreReported()
    {
        var document = Load("""
            <script>
              window.onunhandledrejection = e => document.title = 'unhandled ' + e.reason.message;
              Promise.reject(new Error('boom'));
            </script>
            """);
        Assert.Equal("unhandled boom", document.Title);
    }

    [Fact]
    public void UnhandledRejections_OfAnAsyncFunctionAreReportedToItsGlobal()
    {
        // The async function of the frame awaits a promise that the main window rejects: its promise belongs to the frame
        var document = Load("""
            <iframe></iframe>
            <script>
              const frame = frames[0];
              let reject;
              const promise = new Promise((_, r) => reject = r);
              window.onunhandledrejection = () => document.title += 'window;';
              frame.onunhandledrejection = e => { document.title += 'frame ' + e.reason + ';'; e.preventDefault(); };
              frame.eval('(async p => { await p; })')(promise);
              setTimeout(() => reject('boom'), 0);
            </script>
            """);
        Assert.Equal("frame boom;", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void EventHandlers_UseTheScopeOfTheElementTheFormAndTheDocument()
    {
        var document = Load("""
            <form name=f><input name=i value=v onclick="document.title = [value, f.name, typeof event, this === i].join()"></form>
            <script>document.forms[0].elements[0].click();</script>
            """);
        Assert.Equal("v,f,object,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void OnRealmCreated_ExposesTheGlobalObject()
    {
        using var engine = new HostFunctionsScriptEngine();
        var document = Load("<script>document.title = hostAdd(40, 2);</script>", engine);
        Assert.Equal("42", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void ContentSecurityPolicy_BlocksEval()
    {
        var document = Load("""
            <meta http-equiv="Content-Security-Policy" content="script-src 'unsafe-inline'">
            <script>
              const results = [];
              for (const code of [() => eval('1'), () => new Function('return 1'), () => (0, eval)('1')]) {
                try { code(); results.push('allowed'); } catch (e) { results.push(e.name); }
              }
              document.title = results.join();
            </script>
            """);
        Assert.Equal("EvalError,EvalError,EvalError", document.Title);
    }

    [Fact]
    public void PairIterators_SeeTheChangesMadeDuringTheIteration()
    {
        var document = Load("""
            <script>
              const params = new URLSearchParams('a=1&b=2&c=3');
              const seen = [];
              for (const [key] of params) {
                seen.push(key);
                if (key === 'a') params.delete('b');
              }
              const iterator = params.keys();
              const prototype = Object.getPrototypeOf(iterator);
              const esIteratorPrototype = Object.getPrototypeOf(Object.getPrototypeOf([][Symbol.iterator]()));
              let throws = false;
              try { prototype.next.call({}); } catch (e) { throws = e instanceof TypeError; }
              document.title = [seen.join(''), Object.prototype.toString.call(iterator), Object.getPrototypeOf(prototype) === esIteratorPrototype, throws].join();
            </script>
            """);
        Assert.Equal("ac,[object URLSearchParams Iterator],true,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Css_RegisterProperty()
    {
        var document = Load("""
            <div id=target style='font-size: 10px; --x: 2em'></div>
            <script>
              const results = [];
              CSS.registerProperty({ name: '--x', syntax: '<length>', initialValue: '0px', inherits: false });
              results.push(getComputedStyle(target).getPropertyValue('--x'));
              for (const definition of [{ name: '--x', syntax: '<length>', initialValue: '0px' }, { name: 'x' }, { name: '--y', syntax: '<length>' }]) {
                try { CSS.registerProperty(definition); } catch (e) { results.push(e.name); }
              }
              try { CSS.registerProperty({}); } catch (e) { results.push(e instanceof TypeError); }
              document.title = results.join();
            </script>
            """);
        Assert.Equal("20px,InvalidModificationError,SyntaxError,SyntaxError,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Bindings_ExtendedAttributes()
    {
        var document = Load("""
            <form><input name=method></form>
            <script>
              const results = [];

              // [PutForwards]: the assignment uses the (overridden) getter
              const element = document.createElement('div');
              element.classList = 'a b';
              results.push(element.className);

              // [Replaceable]: a data property is created on the object
              const descriptor = Object.getOwnPropertyDescriptor(window, 'self');
              descriptor.set.call(null, 'replaced');
              results.push(self);

              // NewTarget whose prototype is not an object: the prototype of the interface of its realm
              const newTarget = function () {};
              newTarget.prototype = 7;
              results.push(Object.getPrototypeOf(Reflect.construct(DOMParser, [], newTarget)) === DOMParser.prototype);

              // [LegacyFactoryFunction] subclass
              class CustomImage extends Image {}
              results.push(new CustomImage() instanceof CustomImage);

              // [LegacyOverrideBuiltIns]: a named property does not prevent the assignment of a built-in property
              const form = document.forms[0];
              form.method = 'post';
              const input = form.method;
              input.remove();
              results.push(input.localName, form.method);

              results.push(window.HTMLDocument === Document);
              document.title = results.join();
            </script>
            """);
        Assert.Equal("a b,replaced,true,true,input,post,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Document_NamedProperties()
    {
        var document = Load("""
            <form name=theform></form><img name=logo id=logoid><img name=body><img name=location><img name=two><embed name=two>
            <iframe name=frame></iframe>
            <script>
              const results = [];
              results.push(document.theform === document.forms[0], document.logo === document.images[0], document.logoid === document.logo);
              results.push('theform' in document, Object.hasOwn(document, 'logo'), 'missing' in document, document.missing);

              // [LegacyOverrideBuiltIns]: a named property hides a property of the prototype chain, but not an own property
              // ([LegacyUnforgeable] location)
              results.push(document.body.localName, document.location === location);
              results.push(Object.prototype.toString.call(document.two), document.two.length, document.two === document.two);
              results.push(document.frame === document.querySelector('iframe').contentWindow);

              // The named properties are enumerable, not writable and configurable; they cannot be redefined or deleted
              const descriptor = Object.getOwnPropertyDescriptor(document, 'theform');
              results.push(descriptor.enumerable, descriptor.writable, descriptor.configurable);
              document.theform = 1;
              results.push(document.theform === document.forms[0], delete document.theform, Reflect.defineProperty(document, 'theform', { value: 1 }));
              results.push(Object.getOwnPropertyNames(document).filter(name => name !== 'location').join(' '));

              // The names follow the mutations
              document.forms[0].name = 'renamed';
              results.push(document.theform, document.renamed === document.forms[0]);
              document.forms[0].remove();
              results.push(document.renamed);

              // A document whose named elements exist before its JavaScript object: the unforgeable members stay own properties
              const other = new DOMParser().parseFromString('<img name=location><img name=title>', 'text/html');
              results.push(other.location, other.title.localName);
              document.title = results.join();
            </script>
            """);
        Assert.Equal("true,true,true,true,true,false,,img,true,[object HTMLCollection],2,true,true,true,false,true,true,false,false,theform logoid logo body two frame,,true,,,img", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void DocumentAll_IsAnHtmlDdaObject()
    {
        var document = Load("""
            <img id=logo><img name=two><img name=two>
            <script>
              const all = document.all;
              const results = [typeof all, Boolean(all), all == null, all == undefined, all === undefined, !all, all ? 'truthy' : 'falsy'];
              results.push(Object.getPrototypeOf(all) === HTMLAllCollection.prototype, Object.prototype.toString.call(all), all instanceof HTMLAllCollection);
              results.push(all.length === document.getElementsByTagName('*').length, all[0].localName, all.logo.localName, all.two.length);
              results.push(all('logo').localName, all(0).localName, all(), all('missing'), all.item('logo').localName, all.namedItem('two').length);
              results.push(Reflect.apply(all, null, ['logo']).id, Object.keys(all).includes('logo'), document.all === all);
              document.title = results.join();
            </script>
            """);
        Assert.Equal("undefined,false,true,true,false,true,falsy,true,[object HTMLAllCollection],true,true,html,img,2,img,html,,,img,2,logo,false,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void InterfaceObjects_AreBuiltInFunctions()
    {
        var document = Load("""
            <script>
              const results = [String(Node), String(HTMLElement), String(Image), Function.prototype.toString.call(DOMException)];
              results.push(Node.name, Node.length, Event.length, Object.getPrototypeOf(Element) === Node, Element.prototype.constructor === Element);
              results.push(Object.getOwnPropertyDescriptor(Node, 'prototype').writable, Object.prototype.hasOwnProperty.call(Node, 'caller'));
              try { Event('x'); } catch (e) { results.push(e.constructor.name); }
              try { new Node(); } catch (e) { results.push(e.constructor.name); }
              class MyEvent extends Event { }
              const event = new MyEvent('x');
              results.push(event instanceof MyEvent, event.type);
              class MyImage extends Image { }
              const image = new MyImage(1, 2);
              results.push(image instanceof MyImage, image.width, new Image() instanceof HTMLImageElement, Image.prototype === HTMLImageElement.prototype);
              customElements.define('my-element', class extends HTMLElement { });
              results.push(document.createElement('my-element') instanceof HTMLElement);
              document.title = results.join();
            </script>
            """);
        Assert.Equal("function Node() { [native code] },function HTMLElement() { [native code] },function Image() { [native code] },function DOMException() { [native code] },Node,0,1,true,true,false,false,TypeError,TypeError,true,x,true,1,true,true,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Window_External()
    {
        var document = Load("""
            <script>
              const results = [Object.hasOwn(window, 'external'), external === window.external, external instanceof External];
              results.push(external.AddSearchProvider(), external.IsSearchProviderInstalled());

              // [Replaceable]
              window.external = 1;
              results.push(external);
              document.title = results.join();
            </script>
            """);
        Assert.Equal("true,true,true,,,1", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void StructuredClone()
    {
        var document = Load("""
            <script>
              const value = { a: [1, 2], d: new Date(0), m: new Map([[1, 'x']]) };
              value.self = value;
              const error = new RangeError('message', { cause: 'cause' });
              const clone = structuredClone(value);
              const errorClone = structuredClone(error);
              const errors = [];
              const tryClone = (value, transfer) => { try { structuredClone(value, { transfer }); } catch (e) { errors.push(e.name); } };
              tryClone(() => {});
              tryClone(new Blob(), [new Blob()]);
              const duplicate = new ArrayBuffer(1);
              tryClone(duplicate, [duplicate, duplicate]);
              document.title = [clone !== value, clone.self === clone, clone.a.join(), clone.d.getTime(), clone.m.get(1), errorClone instanceof RangeError, errorClone.cause, structuredClone(undefined), errors.join('|')].join();
            </script>
            """);
        Assert.Equal("true,true,1,2,0,x,true,cause,,DataCloneError|DataCloneError|DataCloneError", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void StructuredClone_TransfersArrayBuffers()
    {
        var document = Load("""
            <script>
              const buffer = new Uint8Array([1, 2, 3]).buffer;
              const view = new Uint8Array(buffer, 1);
              const clone = structuredClone({ buffer, view }, { transfer: [buffer] });
              let detachedError;
              try { structuredClone(buffer, { transfer: [buffer] }); } catch (e) { detachedError = e.name; }

              // A resizable buffer keeps its maximum length, and a length-tracking view still tracks the length of its buffer
              const resizable = new ArrayBuffer(4, { maxByteLength: 8 });
              const tracking = structuredClone(new Uint16Array(resizable), { transfer: [resizable] });
              tracking.buffer.resize(8);
              document.title = [
                buffer.byteLength, buffer.detached, clone.buffer.byteLength, clone.view.buffer === clone.buffer, clone.view.join(), detachedError,
                resizable.detached, tracking.buffer.maxByteLength, tracking.length,
              ].join();
            </script>
            """);
        Assert.Equal("0,true,3,true,2,3,DataCloneError,true,8,4", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void ErrorEvent_HasTheLocationOfTheError()
    {
        var document = Load("""
            <script>
              window.onerror = (message, filename, lineno, colno) => { document.title = [filename, lineno, colno].join(); return true; };
            </script>
            <script>
            undefinedVariable;
            </script>
            """);

        // The line of an inline script of the parser is its line in the document
        Assert.Equal("https://example.com/,5,1", document.Title);
    }

    [Fact]
    public void Bindings_LegacyUnforgeable()
    {
        var document = Load("""
            <iframe name=initial></iframe>
            <script>
              const href = Object.getOwnPropertyDescriptor(location, 'href');
              const toString = Object.getOwnPropertyDescriptor(location, 'toString');
              const valueOf = Object.getOwnPropertyDescriptor(location, 'valueOf');
              const isTrusted = Object.getOwnPropertyDescriptor(new Event('x'), 'isTrusted');
              const sameGetter = Object.getOwnPropertyDescriptor(document, 'location').get === Object.getOwnPropertyDescriptor(new Document(), 'location').get;
              const iframe = document.querySelector('iframe');
              iframe.name = 'changed';
              document.title = [
                href.configurable, typeof href.get, 'href' in Location.prototype,
                toString.writable, toString.configurable, valueOf.value === Object.prototype.valueOf,
                isTrusted.configurable, sameGetter, iframe.contentWindow.name,
              ].join();
            </script>
            """);
        Assert.Equal("false,function,false,false,false,true,false,true,initial", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Hyperlink_JavaScriptUrl_RunsTheScript()
    {
        var document = Load("""
            <a href="javascript:document.title = 'clicked'">x</a>
            <script>document.querySelector('a').click();</script>
            """);
        Assert.Equal("clicked", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Scripts_ModifyTheDocument()
    {
        var document = Load("""
            <p id=p>a</p>
            <script>
              document.getElementById('p').textContent = 'b';
              document.body.appendChild(document.createElement('div')).id = 'created';
              document.title = typeof window + ' ' + (window === self) + ' ' + (document.defaultView === window);
            </script>
            """);
        Assert.Equal("b", document.GetElementById("p")!.TextContent);
        Assert.NotNull(document.GetElementById("created"));
        Assert.Equal("object true true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void EventHandlers_AndListeners()
    {
        var document = Load("""
            <a id=a onclick="document.title += 'handler;'; return false" href=#></a>
            <script>
              document.getElementById('a').addEventListener('click', e => document.title += 'listener:' + e.defaultPrevented + ';');
            </script>
            """);
        var anchor = document.GetElementById("a")!;
        var notCanceled = anchor.DispatchEvent(new Event("click", new EventInit { Cancelable = true }));
        Assert.False(notCanceled);
        Assert.Equal("handler;listener:true;", document.Title);
    }

    [Fact]
    public void EventHandlers_ParametersShadowTheElementProperties()
    {
        // HTMLScriptElement.event is the empty string: the event parameter of the handler shadows it
        var document = Load("""
            <script id=s onclick="document.title = event.type + ' ' + id + ' ' + typeof onclick"></script>
            """);
        document.GetElementById("s")!.DispatchEvent(new Event("click"));
        Assert.Equal("click s function", document.Title);
    }

    [Fact]
    public void EventHandlers_DoNotBindTheirName()
    {
        // The scope of a handler of the window is the global environment: onresize() calls the global function, not the handler
        // itself; the source text and the name of the function are the ones of the attribute (the title collapses the line breaks)
        var document = Load("""
            <body>
            <div id=d onclick="return 1"></div>
            <script>
              function onresize() { document.title += 'global;'; }
              document.body.setAttribute('onresize', 'onresize(); document.title += typeof event + ";";');
              window.dispatchEvent(new Event('resize'));
              var d = document.getElementById('d');
              document.title += d.onclick.name + ';' + d.onclick.toString();
            </script>
            """);
        Assert.Equal("global;object;onclick;function onclick(event) { return 1 }", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Errors_AreReported()
    {
        var document = Load("""
            <script>
              window.onerror = (message, source, line, column, error) => { document.title = message + '|' + (error instanceof TypeError); return true; };
            </script>
            <script>null.x</script>
            """);
        Assert.Contains("null", document.Title, StringComparison.Ordinal);
        Assert.EndsWith("|true", document.Title, StringComparison.Ordinal);
        Assert.Empty(_errors);
    }

    [Fact]
    public void UnhandledErrors_AreGivenToTheHost()
    {
        Load("<script>throw new Error('boom')</script><script>Promise.reject(new Error('rejected'))</script>");
        Assert.Equal(["Error: boom", "Error: rejected"], _errors.Select(e => e.Message));
    }

    [Fact]
    public void Timers_AndMicrotasks()
    {
        var document = Load("""
            <script>
              const log = [];
              setTimeout(() => { log.push('timeout'); document.title = log.join(','); }, 100);
              Promise.resolve().then(() => log.push('promise'));
              queueMicrotask(() => log.push('microtask'));
              requestAnimationFrame(t => log.push('frame ' + (t > 0)));
              log.push('script');
            </script>
            """);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("script,promise,microtask,frame true,timeout", document.Title);
    }

    [Fact]
    public void BuiltInScripts_DoNotRunThePendingMicrotasks()
    {
        // The first use of Response evaluates Fetch.js and Streams.js in the realm, while a promise reaction is pending
        var document = Load("""
            <script>
              const log = [];
              Promise.resolve().then(() => log.push('promise'));
              new Response('body');
              log.push('script');
              queueMicrotask(() => document.title = log.join(','));
            </script>
            """);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("script,promise", document.Title);
    }

    [Fact]
    public void BuiltInScripts_AreEvaluatedInEachRealm()
    {
        var document = Load("""
            <iframe></iframe>
            <script>
              const frame = frames[0];
              new Response('body');
              const response = new frame.Response('body');
              document.title = [Object.getPrototypeOf(response) === frame.Response.prototype, frame.Response !== Response, response instanceof frame.Response].join(',');
            </script>
            """);
        Assert.Equal("true,true,true", document.Title);
    }

    [Fact]
    public void ExternalScripts_AreLoadedByTheLoader()
    {
        var loader = new RecordingLoader();
        loader.Resources["https://example.com/a.js"] = new DocumentResource("document.title = 'external'") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<script src=a.js></script>", loader: loader);
        Assert.Equal("external", document.Title);
    }

    [Fact]
    public void CustomElements()
    {
        var document = Load("""
            <x-counter></x-counter>
            <script>
              customElements.define('x-counter', class extends HTMLElement {
                connectedCallback() { this.textContent = 'connected'; }
              });
            </script>
            """);
        Assert.Equal("connected", document.QuerySelector("x-counter")!.TextContent);
    }

    [Fact]
    public void Iframes_HaveTheirOwnRealm()
    {
        var document = Load("""
            <iframe></iframe>
            <script>
              const frame = frames[0];
              document.title = (frame.Array !== Array) + ' ' + (frame.parent === window) + ' ' + (window.length);
            </script>
            """);
        Assert.Equal("true true 1", document.Title);
    }

    [Fact]
    public void CallbackInterface_TypeErrorIsCreatedAndReportedInTheRealmOfTheCallback()
    {
        // https://webidl.spec.whatwg.org/#call-a-user-objects-operation: the steps run in the associated realm of the
        // callback object (the realm that created a revoked proxy). The objects are created by the eval of the frame.
        var document = Load("""
            <iframe></iframe>
            <script>
              const frame = frames[0];
              const results = [];
              for (const resolver of [frame.eval('({})'), frame.eval('({ lookupNamespaceURI: {} })'), frame.eval('const { proxy, revoke } = Proxy.revocable({}, {}); revoke(); proxy')]) {
                let error;
                const onError = event => { error = event.error; event.preventDefault(); };
                frame.addEventListener('error', onError);
                let exception;
                try { document.evaluate('/foo:bar', document.documentElement, resolver); } catch (e) { exception = e; }
                frame.removeEventListener('error', onError);
                results.push(exception.name + ':' + (error.constructor === frame.TypeError));
              }

              document.title = results.join(' ');
            </script>
            """);
        Assert.Equal("NamespaceError:true NamespaceError:true NamespaceError:true", document.Title);
    }

    [Fact]
    public void ErrorPrototypeToString_UsesTheObjectAsTheReceiver()
    {
        var document = Load("""
            <script>
              let exception;
              try { document.createElement('1'); } catch (e) { exception = e; }
              document.title = (String(exception) === 'InvalidCharacterError: ' + exception.message) + ' | ' + String(new RangeError('r')) + ' | ' + Error.prototype.toString.call({ name: 'N' });
            </script>
            """);
        Assert.Equal("true | RangeError: r | N", document.Title);
    }

    [Fact]
    public void ObjectOwnPropertyKeys_OfAProxy_KeepOnlyTheKeysOfTheRequestedType()
    {
        var document = Load("""
            <script>
              const s = Symbol('s');
              const proxy = new Proxy({ a: 1, [s]: 2 }, { ownKeys(t) { return Reflect.ownKeys(t); } });
              document.title = [
                Object.getOwnPropertyNames(proxy).length,
                Object.getOwnPropertySymbols(proxy)[0] === s,
                Object.keys(proxy).join(),
                Object.values(proxy).join(),
                JSON.stringify(Object.entries(proxy)),
                Object.getOwnPropertyNames(proxy) instanceof Array,
                Object.keys({ b: 1, [s]: 2 }).join(),
                Object.getOwnPropertyNames.name,
              ].join(' | ');
            </script>
            """);
        Assert.Equal("1 | true | a | 1 | [[\"a\",1]] | true | b | getOwnPropertyNames", document.Title);
    }

    #region WebPlatformJavaScript_HostFunctions
    /// <summary>A script engine whose windows have a <c>hostAdd</c> function.</summary>
    private sealed class HostFunctionsScriptEngine : JavaScriptScriptEngine
    {
        protected override void OnRealmCreated(GlobalScope global, JsObject globalObject)
        {
            var realm = Engine.CurrentRealm!;
            globalObject.SetProperty("hostAdd", realm.CreateFunction("hostAdd", 2, (_, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
        }
    }
    #endregion
}
