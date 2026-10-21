using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>Service workers (https://w3c.github.io/ServiceWorker/).</summary>
public sealed class ServiceWorkerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private BrowsingContext CreateContext(ServiceWorkerBackend? backend = null) => _contexts.Create(new BrowsingContextOptions
    {
        ScriptEngine = _engine,
        DocumentLoader = _loader,
        ServiceWorkers = backend,
        UnhandledScriptError = (_, error) =>
        {
            lock (_errors)
            {
                _errors.Add(error);
            }
        },
    });

    private Document Load(string html, ServiceWorkerBackend? backend = null) => Load(CreateContext(backend), html);

    private static Document Load(BrowsingContext context, string html) => context.Navigate("https://example.com/dir/page.html", _ => html);

    private void AddScript(string url, string source, string contentType = "text/javascript")
        => _loader.Resources[url] = new DocumentResource(source) { Headers = [new("Content-Type", contentType)] };

    private void AddPage(string url, string html)
        => _loader.Resources[url] = new DocumentResource(html) { Headers = [new("Content-Type", "text/html")] };

    /// <summary>Runs the event loop until the title of the document is set (the service workers run on their own threads).</summary>
    private async Task<string> WaitForTitleAsync(Func<Document> getDocument)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (getDocument().Title.Length == 0)
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
            if (getDocument().Title.Length == 0)
            {
                await Task.Delay(1);
            }
        }

        return getDocument().Title;
    }

    private Task<string> WaitForTitleAsync(Document document) => WaitForTitleAsync(() => document);

    [Fact]
    public async Task Register_InstallsAndActivatesTheServiceWorker()
    {
        AddScript("https://example.com/dir/sw.js", """
            const events = [];
            self.addEventListener('install', e => events.push('install:' + (e instanceof InstallEvent)));
            self.addEventListener('activate', e => events.push('activate:' + (e instanceof ExtendableEvent)));
            """);
        var document = Load("""
            <script>
              const states = [];
              navigator.serviceWorker.register('sw.js').then(registration => {
                const worker = registration.installing;
                states.push(worker.state);
                worker.onstatechange = () => {
                  states.push(worker.state);
                  if (worker.state === 'activated') {
                    document.title = JSON.stringify({ states, scope: registration.scope, scriptURL: worker.scriptURL, active: registration.active === worker, updateViaCache: registration.updateViaCache });
                  }
                };
              }, e => document.title = 'error: ' + e);
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("""{"states":["installing","installed","activating","activated"],"scope":"https://example.com/dir/","scriptURL":"https://example.com/dir/sw.js","active":true,"updateViaCache":"imports"}""", title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Ready_IsResolvedWithTheActiveRegistration()
    {
        AddScript("https://example.com/dir/sw.js", "");
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sw.js');
              navigator.serviceWorker.ready.then(registration => document.title = registration.active.state + ' ' + (navigator.serviceWorker.ready === navigator.serviceWorker.ready));
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("activating true", title);
    }

    [Fact]
    public async Task Register_RejectsAScriptThatIsNotJavaScript()
    {
        AddScript("https://example.com/dir/sw.txt", "", contentType: "text/plain");
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sw.txt').then(() => document.title = 'resolved', e => document.title = e.name);
            </script>
            """);

        Assert.Equal("SecurityError", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task Register_RejectsAScopeOutsideOfTheDirectoryOfTheScript()
    {
        AddScript("https://example.com/dir/sub/sw.js", "");
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sub/sw.js', { scope: './' }).then(() => document.title = 'resolved', e => document.title = e.name);
            </script>
            """);

        Assert.Equal("SecurityError", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task Register_RejectsAScriptThatThrows()
    {
        AddScript("https://example.com/dir/sw.js", "throw new Error('boom');");
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sw.js').then(() => document.title = 'resolved', e => document.title = e.name);
            </script>
            """);

        Assert.Equal("TypeError", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task GetRegistrationAndUnregister()
    {
        AddScript("https://example.com/dir/sw.js", "");
        var document = Load("""
            <script>
              (async () => {
                const registration = await navigator.serviceWorker.register('sw.js', { scope: 'scope/' });
                const found = await navigator.serviceWorker.getRegistration('scope/page.html');
                const all = await navigator.serviceWorker.getRegistrations();
                const notFound = await navigator.serviceWorker.getRegistration('other.html');
                const result = await registration.unregister();
                const after = await navigator.serviceWorker.getRegistration('scope/page.html');
                document.title = [found === registration, all.length, notFound, result, after].join();
              })().catch(e => document.title = 'error: ' + e);
            </script>
            """);

        Assert.Equal("true,1,,true,", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task ImportScripts_AreStoredWhileInstalling()
    {
        AddScript("https://example.com/dir/sw.js", """
            importScripts('imported.js');
            self.addEventListener('message', e => e.source.postMessage(value));
            """);
        AddScript("https://example.com/dir/imported.js", "var value = 'imported';");
        var document = Load("""
            <script>
              navigator.serviceWorker.onmessage = e => document.title = e.data + ' ' + (e.source instanceof ServiceWorker);
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(registration => registration.active.postMessage('ping'));
            </script>
            """);

        Assert.Equal("imported true", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task WaitUntil_RejectedPromise_FailsTheInstallation()
    {
        AddScript("https://example.com/dir/sw.js", "self.oninstall = e => e.waitUntil(Promise.reject(new Error('fail')));");
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sw.js').then(registration => {
                const worker = registration.installing;
                worker.onstatechange = () => { if (worker.state === 'redundant') setTimeout(() => document.title = 'redundant ' + registration.installing); };
              });
            </script>
            """);

        Assert.Equal("redundant null", await WaitForTitleAsync(document));
    }

    private const string RegisterAndReload = """
        <script>
          const activated = worker => worker.state === 'activated' ? Promise.resolve() : new Promise(resolve => worker.addEventListener('statechange', () => worker.state === 'activated' && resolve()));
          navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(registration => activated(registration.active)).then(() => {
            const frame = document.createElement('iframe');
            frame.src = 'controlled.html';
            frame.onload = () => document.title = frame.contentDocument.body.textContent + '|' + (frame.contentWindow.navigator.serviceWorker.controller !== null);
            document.body.appendChild(frame);
          }, e => document.title = 'error: ' + e);
        </script>
        """;

    [Fact]
    public async Task FetchEvent_RespondWith_AnswersTheNavigation()
    {
        AddScript("https://example.com/dir/sw.js", """
            self.addEventListener('fetch', e => {
              if (e.request.url.endsWith('controlled.html'))
                e.respondWith(new Response('<p>from ' + e.request.mode + ' ' + e.request.destination + ' ' + (e.resultingClientId !== '') + '</p>', { headers: { 'Content-Type': 'text/html' } }));
            });
            """);
        AddPage("https://example.com/dir/controlled.html", "<p>network</p>");
        var document = Load(RegisterAndReload);

        Assert.Equal("from navigate iframe true|true", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task FetchEvent_WithoutRespondWith_FallsBackToTheNetwork()
    {
        AddScript("https://example.com/dir/sw.js", "self.addEventListener('fetch', e => {});");
        AddPage("https://example.com/dir/controlled.html", "<p>network</p>");
        var document = Load(RegisterAndReload);

        Assert.Equal("network|true", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task SoftUpdate_RunsAfterTheDelay_OnceTheNavigationIsLoaded()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = _loader, TimeProvider = clock });
        AddScript("https://example.com/dir/sw.js", "self.addEventListener('fetch', e => {}); // 1");
        AddPage("https://example.com/dir/controlled.html", "<p>network</p>");
        var document = Load(context, """
            <script>
              const events = [];
              const activated = worker => worker.state === 'activated' ? Promise.resolve() : new Promise(resolve => worker.addEventListener('statechange', () => worker.state === 'activated' && resolve()));
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(registration => activated(registration.active).then(() => {
                registration.onupdatefound = () => { events.push('updatefound'); document.title = events.join(); };
                const frame = document.createElement('iframe');
                frame.src = 'controlled.html';
                frame.onload = () => { events.push('load'); document.title = events.join(); };
                document.body.appendChild(frame);
              }));
            </script>
            """);

        // The navigation handled by the service worker schedules a soft update: the script is not fetched again before the delay
        Assert.Equal("load", await WaitForTitleAsync(document));
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, CountScriptRequests());
        Assert.Equal("load", document.Title);

        // The update check finds a new script once the delay has elapsed
        AddScript("https://example.com/dir/sw.js", "self.addEventListener('fetch', e => {}); // 2");
        clock.Advance(TimeSpan.FromSeconds(1));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (document.Title != "load,updatefound" && stopwatch.Elapsed < Timeout)
        {
            await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal("load,updatefound", document.Title);
        Assert.Equal(2, CountScriptRequests());

        int CountScriptRequests()
        {
            lock (_loader.Requests)
            {
                return _loader.Requests.Count(request => request.Url == "https://example.com/dir/sw.js");
            }
        }
    }

    [Fact]
    public async Task FetchEvent_InterceptsTheFetchesOfAControlledDocument()
    {
        AddScript("https://example.com/dir/sw.js", """
            self.addEventListener('fetch', e => {
              const url = new URL(e.request.url);
              if (url.pathname.endsWith('/data.txt'))
                e.respondWith(fetch('real.txt').then(r => r.text()).then(t => new Response(t + ' via sw ' + e.request.headers.get('x-test') + ' ' + (e.clientId !== ''))));
            });
            """);
        AddPage("https://example.com/dir/controlled.html", """
            <script>
              fetch('data.txt', { headers: { 'x-test': 'value' } }).then(r => r.text()).then(t => document.body.textContent = t);
            </script>
            """);
        _loader.Resources["https://example.com/dir/real.txt"] = new DocumentResource("real");
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(() => {
                const frame = document.createElement('iframe');
                frame.src = 'controlled.html';
                document.body.appendChild(frame);
                const check = () => frame.contentDocument?.body?.textContent ? document.title = frame.contentDocument.body.textContent : setTimeout(check, 10);
                check();
              });
            </script>
            """);

        Assert.Equal("real via sw value true", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task FetchEvent_RespondWithANonResponse_IsANetworkError()
    {
        AddScript("https://example.com/dir/sw.js", "self.addEventListener('fetch', e => { if (e.request.url.endsWith('data.txt')) e.respondWith(Promise.resolve('not a response')); });");
        AddPage("https://example.com/dir/controlled.html", """
            <script>
              fetch('data.txt').then(() => document.body.textContent = 'resolved', e => document.body.textContent = e.name);
            </script>
            """);
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(() => {
                const frame = document.createElement('iframe');
                frame.src = 'controlled.html';
                document.body.appendChild(frame);
                const check = () => frame.contentDocument?.body?.textContent ? document.title = frame.contentDocument.body.textContent : setTimeout(check, 10);
                check();
              });
            </script>
            """);

        Assert.Equal("TypeError", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task NavigationPreload_GivesThePreloadResponse()
    {
        AddScript("https://example.com/dir/sw.js", """
            self.addEventListener('activate', e => e.waitUntil(self.registration.navigationPreload.enable()));
            self.addEventListener('fetch', e => {
              if (e.request.url.endsWith('controlled.html'))
                e.respondWith(e.preloadResponse.then(r => r.text()).then(t => new Response('<p>preload: ' + t + '</p>', { headers: { 'Content-Type': 'text/html' } })));
            });
            """);
        AddPage("https://example.com/dir/controlled.html", "network");
        var document = Load(RegisterAndReload);

        Assert.Equal("preload: network|true", await WaitForTitleAsync(document));
        Assert.Contains(_loader.Requests, request => request.Url == "https://example.com/dir/controlled.html" && request.Headers.Any(header => header.Key == "Service-Worker-Navigation-Preload" && header.Value == "true"));
    }

    [Fact]
    public async Task Claim_ControlsTheExistingClients()
    {
        AddScript("https://example.com/dir/sw.js", "self.addEventListener('activate', e => e.waitUntil(clients.claim()));");
        var document = Load("""
            <script>
              navigator.serviceWorker.oncontrollerchange = () => document.title = 'controlled by ' + navigator.serviceWorker.controller.scriptURL;
              navigator.serviceWorker.register('sw.js');
            </script>
            """);

        Assert.Equal("controlled by https://example.com/dir/sw.js", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task ClientsMatchAll_AndPostMessage()
    {
        AddScript("https://example.com/dir/sw.js", """
            self.addEventListener('message', async e => {
              const all = await clients.matchAll({ includeUncontrolled: true });
              for (const client of all) client.postMessage(all.length + ' ' + client.type + ' ' + client.frameType + ' ' + client.url + ' ' + (e.source.id === client.id));
            });
            """);
        var document = Load("""
            <script>
              navigator.serviceWorker.onmessage = e => document.title = e.data;
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(r => r.active.postMessage('hello'));
            </script>
            """);

        Assert.Equal("1 window top-level https://example.com/dir/page.html true", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task Registrations_ArePersistedInTheBackend()
    {
        AddScript("https://example.com/dir/sw.js", """
            self.addEventListener('fetch', e => {
              if (e.request.url.endsWith('controlled.html'))
                e.respondWith(new Response('<p>from sw</p>', { headers: { 'Content-Type': 'text/html' } }));
            });
            """);
        AddPage("https://example.com/dir/controlled.html", "<p>network</p>");
        var path = Path.Combine(Path.GetTempPath(), "service-workers-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var backend = new Storage.SqliteServiceWorkerBackend(path))
            {
                var document = Load("<script>navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(() => document.title = 'ready');</script>", backend);
                Assert.Equal("ready", await WaitForTitleAsync(document));
            }

            // A new browsing context (e.g. a new process) reads the registration and runs the stored script
            _loader.Resources.Remove("https://example.com/dir/sw.js");
            using (var backend = new Storage.SqliteServiceWorkerBackend(path))
            {
                var context = CreateContext(backend);
                var document = context.Navigate("https://example.com/dir/page2.html", _ => "<iframe src='controlled.html' onload='document.title = frames[0].document.body.textContent'></iframe>");
                Assert.Equal("from sw", await WaitForTitleAsync(document));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnregisterThenRegister_HasAnInstallingWorker()
    {
        AddScript("https://example.com/dir/resources/worker.js", "self.onfetch = e => {};");
        var document = Load("""
            <script>
              const scope = './resources/iframe.html';
              navigator.serviceWorker.getRegistration(scope)
                .then(r => r ? r.unregister() : undefined)
                .then(() => navigator.serviceWorker.register('./resources/worker.js', { scope }))
                .then(r => document.title = String(r.installing) + ' ' + (r.installing && r.installing.state), e => document.title = 'error ' + e);
            </script>
            """);

        Assert.Equal("[object ServiceWorker] installing", await WaitForTitleAsync(document));
    }

    private const string WaitForState = "const state = (worker, value) => worker.state === value ? Promise.resolve() : new Promise(resolve => worker.addEventListener('statechange', () => worker.state === value && resolve()));";

    [Fact]
    public async Task DynamicImport_IsRejectedInAServiceWorker()
    {
        AddScript("https://example.com/dir/module.js", "export default 1;");
        AddScript("https://example.com/dir/sw.js", "self.addEventListener('message', e => import('./module.js').then(() => e.source.postMessage('imported'), error => e.source.postMessage(error.name)));");
        var document = Load("""
            <script>
              navigator.serviceWorker.onmessage = e => document.title = e.data;
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(registration => registration.active.postMessage('ping'));
            </script>
            """);

        Assert.Equal("TypeError", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task ModuleServiceWorker_StaticImportsAreStoredAndFetchedWithoutCredentials()
    {
        AddScript("https://example.com/dir/sw.js", """
            import { value } from './dependency.js';
            self.addEventListener('message', e => e.source.postMessage(value));
            """);
        AddScript("https://example.com/dir/dependency.js", "export const value = 'dependency';");
        var document = Load("""
            <script>
              navigator.serviceWorker.onmessage = e => document.title = e.data;
              navigator.serviceWorker.register('sw.js', { type: 'module' }).then(() => navigator.serviceWorker.ready).then(registration => registration.active.postMessage('ping'));
            </script>
            """);

        Assert.Equal("dependency", await WaitForTitleAsync(document));
        DocumentRequest[] requests;
        lock (_loader.Requests)
        {
            requests = [.. _loader.Requests.Where(request => request.Url.EndsWith(".js", StringComparison.Ordinal))];
        }

        // The module graph is fetched with the credentials mode "omit"; the import is a CORS request whose destination is the worker
        var main = Assert.Single(requests, request => request.Url == "https://example.com/dir/sw.js");
        var import = Assert.Single(requests, request => request.Url == "https://example.com/dir/dependency.js");
        Assert.False(main.IncludeCredentials);
        Assert.False(import.IncludeCredentials);
        Assert.Equal(RequestDestination.ServiceWorker, import.Destination);
        Assert.Equal(RequestMode.Cors, import.Mode);
    }

    [Fact]
    public async Task ModuleServiceWorker_UpdateChecksTheStaticImports()
    {
        AddScript("https://example.com/dir/sw.js", "import './dependency.js';");
        AddScript("https://example.com/dir/dependency.js", "// 1");
        var document = Load("""
            <script>
              navigator.serviceWorker.register('sw.js', { type: 'module' }).then(() => navigator.serviceWorker.ready).then(registration => {
                registration.addEventListener('updatefound', () => document.title = 'updatefound ' + registration.installing.scriptURL);
                document.title = 'ready';
                document.onclick = () => registration.update().then(() => { if (registration.installing === null && document.title === '') document.title = 'no update'; });
              });
            </script>
            """);

        Assert.Equal("ready", await WaitForTitleAsync(document));

        // The scripts did not change
        document.Title = "";
        document.DispatchEvent(new MouseEvent("click"));
        Assert.Equal("no update", await WaitForTitleAsync(document));

        AddScript("https://example.com/dir/dependency.js", "// 2");
        document.Title = "";
        document.DispatchEvent(new MouseEvent("click"));
        Assert.Equal("updatefound https://example.com/dir/sw.js", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task ModuleServiceWorker_EvaluationErrorsRejectTheRegistration()
    {
        AddScript("https://example.com/dir/throw.js", "throw new Error('boom');");
        AddScript("https://example.com/dir/await.js", "await Promise.resolve(1);");
        AddScript("https://example.com/dir/exported.js", "export const value = 1;");
        AddScript("https://example.com/dir/link.js", "import missing from './exported.js';");
        AddScript("https://example.com/dir/valid.js", "try { throw new Error('caught'); } catch { }");
        var document = Load("""
            <script>
              Promise.allSettled(['throw.js', 'await.js', 'link.js', 'valid.js'].map(script => navigator.serviceWorker.register(script, { type: 'module', scope: script + '-scope' })))
                .then(results => document.title = results.map(result => result.status === 'fulfilled' ? 'registered' : result.reason.name).join());
            </script>
            """);

        Assert.Equal("TypeError,TypeError,TypeError,registered", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task Register_RejectsARedirectedScript()
    {
        AddScript("https://example.com/dir/sw.js", "");
        _loader.Resources["https://example.com/dir/redirect.js"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "https://example.com/dir/sw.js")] };
        var document = Load("""
            <script>
              navigator.serviceWorker.register('redirect.js').then(() => document.title = 'resolved', e => document.title = e.name);
            </script>
            """);

        Assert.Equal("SecurityError", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task SkipWaiting_PromiseIsResolvedAfterTheActivateEventAndBeforeTheActivatedState()
    {
        AddScript("https://example.com/dir/sw1.js", "");
        AddScript("https://example.com/dir/sw2.js", """
            let sawActivate = false;
            self.addEventListener('activate', () => sawActivate = true);
            self.addEventListener('message', e => e.waitUntil(self.skipWaiting().then(() => e.source.postMessage(sawActivate + ' ' + self.registration.active.state))));
            """);
        AddPage("https://example.com/dir/scope/controlled.html", "<p>network</p>");
        var document = Load($$"""
            <script>
              {{WaitForState}}
              (async () => {
                const first = await navigator.serviceWorker.register('sw1.js', { scope: 'scope/' });
                await state(first.installing, 'activated');
                const frame = document.createElement('iframe');
                frame.src = 'scope/controlled.html';
                await new Promise(resolve => { frame.onload = resolve; document.body.appendChild(frame); });
                const controller = [];
                frame.contentWindow.navigator.serviceWorker.oncontrollerchange = e => controller.push(e.target.controller.state);
                const second = await navigator.serviceWorker.register('sw2.js', { scope: 'scope/' });
                const worker = second.installing;
                await state(worker, 'installed');
                navigator.serviceWorker.onmessage = e => document.title = e.data + ' ' + controller;
                worker.postMessage('skip');
              })().catch(e => document.title = 'error: ' + e);
            </script>
            """);

        // The new controller is still activating when the controllerchange event is fired
        Assert.Equal("true activating activating", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task Unregister_ClearsTheRegistrationOnceTheWorkerHasNoPendingEvents()
    {
        AddScript("https://example.com/dir/sw.js", """
            let resolve;
            const pending = new Promise(r => resolve = r);
            self.onmessage = e => {
              if (e.data === 'wait')
                e.waitUntil(pending);
              else
                resolve();
            };
            """);
        var document = Load($$"""
            <script>
              {{WaitForState}}
              (async () => {
                const registration = await navigator.serviceWorker.register('sw.js', { scope: 'scope/' });
                const worker = registration.installing;
                await state(worker, 'activated');
                worker.postMessage('wait');
                await registration.unregister();
                const states = [worker.state, registration.active === worker];
                worker.onstatechange = () => document.title = [...states, worker.state, registration.installing, registration.waiting, registration.active].join();
                worker.postMessage('go');
              })().catch(e => document.title = 'error: ' + e);
            </script>
            """);

        // The registration is cleared when the pending event ends: its workers are removed before the statechange event
        Assert.Equal("activated,true,redundant,,,", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task FetchEvent_RequestOfARedirectedNavigationHasTheCurrentUrl()
    {
        AddScript("https://example.com/dir/sw.js", """
            self.addEventListener('fetch', e => e.respondWith(new Response('<p>' + [e.request.url, e.request.mode].join(' ') + '</p>', { headers: { 'Content-Type': 'text/html' } })));
            """);
        _loader.Resources["https://example.com/dir/redirect"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "https://example.com/dir/scope/target.html")] };
        var document = Load($$"""
            <script>
              {{WaitForState}}
              (async () => {
                const registration = await navigator.serviceWorker.register('sw.js', { scope: 'scope/' });
                await state(registration.installing, 'activated');
                const frame = document.createElement('iframe');
                frame.src = 'redirect';
                frame.onload = () => document.title = frame.contentDocument.body.textContent + ' ' + frame.contentWindow.location.href;
                document.body.appendChild(frame);
              })().catch(e => document.title = 'error: ' + e);
            </script>
            """);

        Assert.Equal("https://example.com/dir/scope/target.html navigate https://example.com/dir/scope/target.html", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task GlobalObject_FetchIsInheritedAndThePrototypesAreImmutable()
    {
        AddScript("https://example.com/dir/sw.js", """
            self.addEventListener('message', e => {
              const prototype = Object.getPrototypeOf(self);
              const workerGlobalScopePrototype = Object.getPrototypeOf(prototype);
              let thrown = false;
              try { Object.setPrototypeOf(self, {}); } catch (error) { thrown = error instanceof TypeError; }
              e.source.postMessage([
                self.hasOwnProperty('fetch'),
                workerGlobalScopePrototype === WorkerGlobalScope.prototype && workerGlobalScopePrototype.hasOwnProperty('fetch'),
                thrown,
                Reflect.setPrototypeOf(prototype, {}),
                Reflect.setPrototypeOf(self, prototype),
                Object.getPrototypeOf(self) === ServiceWorkerGlobalScope.prototype,
                Reflect.setPrototypeOf({}, null),
              ].join());
            });
            """);
        var document = Load("""
            <script>
              navigator.serviceWorker.onmessage = e => document.title = e.data;
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(registration => registration.active.postMessage('ping'));
            </script>
            """);

        Assert.Equal("false,true,true,false,true,true,true", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task PostMessage_ThrowsForTheServiceWorkerObjectOfARemovedIframe()
    {
        AddScript("https://example.com/dir/sw.js", "");
        AddPage("https://example.com/dir/scope/controlled.html", "<p>network</p>");
        var document = Load($$"""
            <script>
              {{WaitForState}}
              (async () => {
                const registration = await navigator.serviceWorker.register('sw.js', { scope: 'scope/' });
                await state(registration.installing, 'activated');
                const frame = document.createElement('iframe');
                frame.src = 'scope/controlled.html';
                await new Promise(resolve => { frame.onload = resolve; document.body.appendChild(frame); });
                const worker = frame.contentWindow.navigator.serviceWorker.controller;
                frame.remove();
                try {
                  worker.postMessage('');
                  document.title = 'posted';
                } catch (e) {
                  document.title = e.name + ' ' + worker.state;
                }
              })().catch(e => document.title = 'error: ' + e);
            </script>
            """);

        Assert.Equal("InvalidStateError activated", await WaitForTitleAsync(document));
    }

    [Fact]
    public async Task InitialAboutBlankIframe_IsControlledByTheServiceWorkerOfItsParent()
    {
        AddScript("https://example.com/dir/sw.js", "");
        AddPage("https://example.com/dir/scope/controlled.html", "<iframe id=nested></iframe><script>onload = () => parent.postMessage(String(document.getElementById('nested').contentWindow.navigator.serviceWorker.controller && navigator.serviceWorker.controller.scriptURL), '*');</script>");
        var document = Load($$"""
            <script>
              {{WaitForState}}
              onmessage = e => document.title = e.data;
              (async () => {
                const registration = await navigator.serviceWorker.register('sw.js', { scope: 'scope/' });
                await state(registration.installing, 'activated');
                const frame = document.createElement('iframe');
                frame.src = 'scope/controlled.html';
                document.body.appendChild(frame);
              })().catch(e => document.title = 'error: ' + e);
            </script>
            """);

        Assert.Equal("https://example.com/dir/sw.js", await WaitForTitleAsync(document));
    }
}
