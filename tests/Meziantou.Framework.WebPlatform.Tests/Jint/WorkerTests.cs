using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>Dedicated and shared workers (https://html.spec.whatwg.org/multipage/workers.html).</summary>
public sealed class WorkerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) =>
            {
                lock (_errors)
                {
                    _errors.Add(error);
                }
            },
        });

        return context.Navigate("https://example.com/dir/page.html", _ => html);
    }

    private void AddScript(string url, string source, string contentType = "text/javascript")
        => _loader.Resources[url] = new DocumentResource(source) { Headers = [new("Content-Type", contentType)] };

    /// <summary>Runs the event loop until the title of the document is set (the workers run on their own threads).</summary>
    private async Task<string> WaitForTitleAsync(Document document)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (document.Title.Length == 0)
        {
            if (stopwatch.Elapsed > Timeout)
            {
                lock (_errors)
                {
                    Assert.Fail("Timeout. Errors: " + string.Join(" | ", _errors.Select(e => e.Message + " " + e.Filename + ":" + e.LineNumber)));
                }
            }

            using var cancellation = new CancellationTokenSource(Timeout);
            await _contexts.EventLoop.RunUntilIdleAsync(cancellation.Token);
            if (document.Title.Length == 0)
            {
                await Task.Delay(1);
            }
        }

        return document.Title;
    }

    [Fact]
    public async Task Worker_PostMessage_RoundTrip()
    {
        AddScript("https://example.com/dir/worker.js", """
            onmessage = e => postMessage({ echo: e.data, self: self === globalThis, type: String(self), location: location.href, name: self.name });
            """);
        var document = Load("""
            <script>
              const worker = new Worker('worker.js', { name: 'w1' });
              worker.onmessage = e => document.title = JSON.stringify(e.data);
              worker.postMessage([1, 'two', { three: 3 }]);
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("""{"echo":[1,"two",{"three":3}],"self":true,"type":"[object DedicatedWorkerGlobalScope]","location":"https://example.com/dir/worker.js","name":"w1"}""", title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Worker_ExposesOnlyTheWorkerInterfaces()
    {
        AddScript("https://example.com/dir/worker.js", """
            postMessage([typeof document, typeof window, typeof Node, typeof URL, typeof TextEncoder, typeof fetch, typeof ReadableStream, typeof WorkerGlobalScope, typeof DedicatedWorkerGlobalScope, typeof importScripts, typeof setTimeout, typeof console].join());
            """);
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("undefined,undefined,undefined,function,function,function,function,function,function,function,function,object", title);
    }

    [Fact]
    public async Task Worker_UnhandledError_IsFiredAtTheWorker()
    {
        AddScript("https://example.com/dir/worker.js", "throw new TypeError('boom');");
        var document = Load("""
            <script>
              new Worker('worker.js').onerror = e => { e.preventDefault(); document.title = e.message + ' ' + e.filename + ' ' + (e.error === null); };
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("TypeError: boom https://example.com/dir/worker.js true", title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Worker_ScriptLoadFailure_FiresAnErrorEvent()
    {
        AddScript("https://example.com/dir/worker.txt", "postMessage(1);", contentType: "text/plain");
        var document = Load("""
            <script>
              const results = [];
              const done = () => { if (results.length === 2) document.title = results.sort().join(); };
              new Worker('missing.js').onerror = e => { results.push('missing ' + e.constructor.name); done(); };
              new Worker('worker.txt').onerror = e => { results.push('mime ' + e.constructor.name); done(); };
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("mime Event,missing Event", title);
    }

    [Fact]
    public async Task Worker_ImportScripts_AndTimers()
    {
        AddScript("https://example.com/dir/lib.js", "self.value = (self.value || 0) + 40;");
        AddScript("https://example.com/dir/worker.js", """
            importScripts('lib.js', 'lib.js');
            setTimeout(() => postMessage(value + 2), 10);
            """);
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = String(e.data);</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("82", title);
    }

    [Fact]
    public async Task Worker_Terminate_AbortsAnInfiniteLoop()
    {
        AddScript("https://example.com/dir/worker.js", "postMessage('started'); while (true) { }");
        var document = Load("""
            <script>
              const worker = new Worker('worker.js');
              worker.onmessage = () => { worker.terminate(); setTimeout(() => document.title = 'terminated', 0); };
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("terminated", title);
    }

    [Fact]
    public async Task Worker_TransfersArrayBuffersAndMessagePorts()
    {
        AddScript("https://example.com/dir/worker.js", """
            onmessage = e => {
              const port = e.ports[0];
              port.postMessage(new Uint8Array(e.data.buffer).join('-'));
            };
            """);
        var document = Load("""
            <script>
              const worker = new Worker('worker.js');
              const channel = new MessageChannel();
              const buffer = new Uint8Array([1, 2, 3]).buffer;
              channel.port1.onmessage = e => document.title = e.data + ' ' + buffer.byteLength;
              worker.postMessage({ buffer }, [buffer, channel.port2]);
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("1-2-3 0", title);
    }

    [Fact]
    public async Task ModuleWorker_ImportsModules()
    {
        AddScript("https://example.com/dir/lib.js", "export const value = 'module';");
        AddScript("https://example.com/dir/worker.js", "import { value } from './lib.js'; postMessage(value + ' ' + typeof importScripts);");
        var document = Load("<script>new Worker('worker.js', { type: 'module' }).onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("module function", title);
    }

    [Fact]
    public async Task NestedWorker()
    {
        AddScript("https://example.com/dir/inner.js", "postMessage('inner ' + location.pathname);");
        AddScript("https://example.com/dir/outer.js", "new Worker('inner.js').onmessage = e => postMessage('outer/' + e.data);");
        var document = Load("<script>new Worker('outer.js').onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("outer/inner /dir/inner.js", title);
    }

    [Fact]
    public async Task SharedWorker_ConnectsSeveralObjects()
    {
        AddScript("https://example.com/dir/shared.js", """
            let count = 0;
            onconnect = e => { count++; e.ports[0].postMessage(count + ' ' + (e.source === e.ports[0]) + ' ' + name); };
            """);
        var document = Load("""
            <script>
              const results = [];
              for (const i of [1, 2]) {
                const worker = new SharedWorker('shared.js', 'n');
                worker.port.onmessage = e => { results.push(e.data); if (results.length === 2) document.title = results.join('|'); };
              }
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("1 true n|2 true n", title);
    }

    [Fact]
    public async Task Worker_DataUrl_HasAnOpaqueOrigin()
    {
        var document = Load("""
            <script>
              new Worker('data:text/javascript,postMessage(self.origin + " " + location.protocol)').onmessage = e => document.title = e.data;
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("null data:", title);
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("module")]
    public async Task Worker_DataUrl_ImportsAndFetchesCrossOriginResources(string type)
    {
        _loader.Resources["https://example.com/dir/data.txt"] = new DocumentResource("hello") { Headers = [new("Content-Type", "text/plain"), new("Access-Control-Allow-Origin", "*")] };
        _loader.Resources["https://example.com/dir/lib.js"] = new DocumentResource("self.lib = 1;") { Headers = [new("Content-Type", "text/javascript"), new("Access-Control-Allow-Origin", "*")] };
        var document = Load($$"""
            <script>
              new Worker('data:text/javascript,' + encodeURIComponent((('{{type}}' === 'classic') ? 'importScripts("https://example.com/dir/lib.js");' : 'import "https://example.com/dir/lib.js";') + 'fetch("https://example.com/dir/data.txt").then(r => r.text()).then(t => postMessage(t), e => postMessage("err " + e))'), { type: '{{type}}' }).onmessage = e => document.title = e.data;
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("hello", title);
    }

    [Fact]
    public async Task Worker_FetchAndXmlHttpRequest()
    {
        AddScript("https://example.com/dir/data.txt", "hello", contentType: "text/plain");
        AddScript("https://example.com/dir/worker.js", """
            const xhr = new XMLHttpRequest();
            xhr.open('GET', 'data.txt', false);
            xhr.send();
            fetch('data.txt').then(r => r.text()).then(text => postMessage(xhr.status + ' ' + xhr.responseText + ' ' + text + ' ' + typeof xhr.responseXML));
            """);
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("200 hello hello undefined", title);
    }

    [Fact]
    public async Task Worker_BlobUrls_AreSharedWithTheWindow()
    {
        var document = Load("""
            <script>
              const url = URL.createObjectURL(new Blob(['onmessage = e => fetch(e.data).then(r => r.text()).then(t => postMessage(t + " " + URL.createObjectURL(new Blob(["x"])).startsWith("blob:https://example.com/")));'], { type: 'text/javascript' }));
              const worker = new Worker(url);
              worker.onmessage = e => document.title = e.data;
              worker.postMessage(URL.createObjectURL(new Blob(['from window'])));
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("from window true", title);
    }

    [Fact]
    public async Task BroadcastChannel_BetweenAWindowAndAWorker()
    {
        AddScript("https://example.com/dir/worker.js", """
            const channel = new BroadcastChannel('c');
            channel.onmessage = e => channel.postMessage('worker got ' + e.data);
            postMessage('ready');
            """);
        var document = Load("""
            <script>
              const channel = new BroadcastChannel('c');
              channel.onmessage = e => document.title = e.data;
              new Worker('worker.js').onmessage = () => channel.postMessage('ping');
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("worker got ping", title);
    }

    [Fact]
    public async Task Worker_TransfersBothPortsOfAChannel()
    {
        AddScript("https://example.com/dir/worker.js", """
            const channel = new MessageChannel();
            channel.port1.postMessage('queued before the transfer');
            postMessage(null, [channel.port1, channel.port2]);
            """);
        var document = Load("""
            <script>
              new Worker('worker.js').onmessage = e => {
                const [port1, port2] = e.ports;
                const received = [];
                port2.onmessage = m => { received.push(m.data); if (received.length === 2) document.title = received.join('|'); };
                port1.postMessage('after the transfer');
              };
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("queued before the transfer|after the transfer", title);
    }

    private void AddScript(string url, string source, params KeyValuePair<string, string>[] headers)
        => _loader.Resources[url] = new DocumentResource(source) { Headers = [new("Content-Type", "text/javascript"), .. headers] };

    [Fact]
    public async Task Worker_TrustedTypes_SinksOfTheWorker()
    {
        AddScript("https://example.com/dir/imported.js", "self.imported = (self.imported || 0) + 1;");
        AddScript("https://example.com/dir/worker.js", """
            const results = [];
            const check = (name, fn) => { try { fn(); results.push(name + ':ok'); } catch (e) { results.push(name + ':' + e.name); } };
            const policy = trustedTypes.createPolicy('p', { createScript: s => s, createScriptURL: s => s });
            check('importScripts(string)', () => importScripts('imported.js'));
            check('importScripts(trusted)', () => importScripts(policy.createScriptURL('imported.js')));
            check('setTimeout(string)', () => setTimeout('1'));
            check('setInterval(string)', () => setInterval('1'));
            check('setTimeout(trusted)', () => clearTimeout(setTimeout(policy.createScript('1'))));
            check('eval(string)', () => eval('1'));
            check('eval(trusted)', () => { if (eval(policy.createScript('1 + 1')) !== 2) throw new Error('value'); });
            check('Function(string)', () => new Function('return 1'));
            check('Worker(string)', () => new Worker('nested.js'));
            trustedTypes.createPolicy('default', { createScriptURL: (s, type, sink) => { results.push(type + '|' + sink); return s; } });
            check('importScripts(default)', () => importScripts('imported.js'));
            postMessage(results.join() + ' imported=' + self.imported);
            """, new KeyValuePair<string, string>("Content-Security-Policy", "require-trusted-types-for 'script'"));
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("importScripts(string):TypeError,importScripts(trusted):ok,setTimeout(string):TypeError,setInterval(string):TypeError,setTimeout(trusted):ok,eval(string):EvalError,eval(trusted):ok,Function(string):EvalError,Worker(string):TypeError,TrustedScriptURL|WorkerGlobalScope importScripts,importScripts(default):ok imported=2", title);
    }

    [Fact]
    public async Task Worker_TrustedTypes_PolicyNamesAndViolationEvents()
    {
        AddScript("https://example.com/dir/worker.js", """
            const results = [];
            self.addEventListener('securitypolicyviolation', e => {
              results.push(e.effectiveDirective + '|' + e.blockedURI + '|' + e.sample + '|' + e.disposition);
              if (results.length === 4) postMessage(results.join());
            });
            trustedTypes.createPolicy('one', {});
            try { trustedTypes.createPolicy('one', {}); } catch (e) { results.push('duplicate:' + e.name); }
            try { trustedTypes.createPolicy('two', {}); } catch (e) { results.push('name:' + e.name); }
            """, new KeyValuePair<string, string>("Content-Security-Policy", "trusted-types one"));
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("duplicate:TypeError,name:TypeError,trusted-types|trusted-types-policy|one|enforce,trusted-types|trusted-types-policy|two|enforce", title);
    }

    [Fact]
    public async Task Worker_TrustedTypes_ThePoliciesOfTheWindowDoNotApplyToAnHttpWorker()
    {
        AddScript("https://example.com/dir/worker.js", """
            let result = typeof trustedTypes.createPolicy + ',' + (trustedTypes.defaultPolicy === null);
            try { importScripts('data:text/javascript,self.imported = true'); result += ',imported=' + self.imported; } catch (e) { result += ',' + e.name; }
            postMessage(result);
            """);
        var document = Load("""
            <meta http-equiv="Content-Security-Policy" content="require-trusted-types-for 'script'">
            <script>
              const policy = trustedTypes.createPolicy('p', { createScriptURL: s => s });
              try { new Worker('worker.js'); } catch (e) { document.documentElement.dataset.error = e.name; }
              new Worker(policy.createScriptURL('worker.js')).onmessage = e => document.title = e.data;
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("function,true,imported=true", title);
        Assert.Equal("TypeError", document.DocumentElement!.GetAttribute("data-error"));
    }

    [Fact]
    public async Task Worker_TrustedTypes_ABlobWorkerInheritsThePoliciesOfItsCreator()
    {
        var document = Load("""
            <meta http-equiv="Content-Security-Policy" content="require-trusted-types-for 'script'">
            <script>
              const policy = trustedTypes.createPolicy('p', { createScriptURL: s => s });
              const source = "let result; try { setTimeout('1'); result = 'ok'; } catch (e) { result = e.name; } postMessage(result + ',' + (trustedTypes.defaultPolicy === null));";
              const url = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
              new Worker(policy.createScriptURL(url)).onmessage = e => document.title = e.data;
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("TypeError,true", title);
    }

    [Fact]
    public async Task Worker_Fonts_LoadsAFontFace()
    {
        _loader.Resources["https://example.com/dir/font.ttf"] = new DocumentResource(TestFonts.Ahem) { Headers = [new("Content-Type", "font/ttf")] };
        AddScript("https://example.com/dir/worker.js", """
            (async () => {
              const results = [String(self.fonts), self.fonts.status, self.fonts.size];
              const events = [];
              self.fonts.onloading = () => events.push('loading');
              self.fonts.onloadingdone = e => events.push('loadingdone:' + e.fontfaces.length);

              const face = new FontFace('WorkerFont', 'url(font.ttf)');
              results.push(face.status, self.fonts.check('20px WorkerFont'));
              self.fonts.add(face);
              results.push(self.fonts.has(face), self.fonts.size, self.fonts.check('20px WorkerFont'));
              const loaded = face.load();
              results.push(face.status, self.fonts.status);
              results.push((await self.fonts.ready) === self.fonts, (await loaded) === face, face.status, self.fonts.status, self.fonts.check('20px WorkerFont'));
              results.push((await self.fonts.load('20px WorkerFont')).length);

              await new Promise(resolve => setTimeout(resolve, 0));
              results.push(events.join('+'));
              results.push(self.fonts.delete(face), self.fonts.size);
              postMessage(results.join());
            })().catch(e => postMessage('error: ' + e));
            """);
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("[object FontFaceSet],loaded,0,unloaded,true,true,1,false,loading,loading,true,true,loaded,loaded,true,1,loading+loadingdone:1,true,0", title);
        lock (_loader.Requests)
        {
            var request = Assert.Single(_loader.Requests, request => request.Url == "https://example.com/dir/font.ttf");
            Assert.Equal(RequestDestination.Font, request.Destination);
        }
    }

    [Fact]
    public async Task Worker_Fonts_BinaryDataAndErrors()
    {
        var data = Convert.ToBase64String(TestFonts.Ahem);
        AddScript("https://example.com/dir/worker.js", $$"""
            (async () => {
              const results = [];
              const bytes = Uint8Array.from(atob('{{data}}'), c => c.charCodeAt(0));
              const face = new FontFace('Binary', bytes.buffer);
              self.fonts.add(face);

              // The data is loaded by a task: the font face is "unloaded" until then, and the set is not loading yet
              results.push(face.status, self.fonts.status);
              await face.loaded;
              await self.fonts.ready;
              results.push(face.status, self.fonts.status);

              const missing = new FontFace('Missing', 'url(missing.ttf)');
              self.fonts.add(missing);
              const errors = new Promise(resolve => self.fonts.onloadingerror = e => resolve(e.fontfaces.length));
              try { await missing.load(); results.push('loaded'); } catch (e) { results.push(e.name); }
              results.push(missing.status, await errors, self.fonts.check('10px Missing'));

              const invalid = new FontFace('Invalid', 'url(font.ttf)', { weight: 'not a weight' });
              try { await invalid.loaded; results.push('loaded'); } catch (e) { results.push(e.name); }
              try { self.fonts.check('not a font'); } catch (e) { results.push(e.name); }
              postMessage(results.join());
            })().catch(e => postMessage('error: ' + e));
            """);
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = e.data;</script>");

        var title = await WaitForTitleAsync(document);
        Assert.Equal("unloaded,loaded,loaded,loaded,NetworkError,error,1,false,SyntaxError,SyntaxError", title);
    }
}
