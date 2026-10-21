using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>Deferred fetching (https://fetch.spec.whatwg.org/#deferred-fetch): <c>fetchLater()</c>.</summary>
public sealed class FetchLaterTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private BrowsingContext Load(string html, string url = "https://example.com/", TimeProvider? timeProvider = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            TimeProvider = timeProvider,
        });

        context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return context;
    }

    private List<DocumentRequest> RequestsOf(string url)
    {
        lock (_loader.Requests)
        {
            return [.. _loader.Requests.Where(request => request.Url == url)];
        }
    }

    [Fact]
    public async Task Request_IsSentAfterActivateAfter()
    {
        _loader.Resources["https://example.com/report"] = new DocumentResource("");
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var context = Load("""
            <script>
              window.result = fetchLater('/report', { method: 'POST', body: 'data', activateAfter: 5000 });
              document.title = String(result instanceof FetchLaterResult) + ' ' + result.activated;
              setTimeout(() => { document.title = 'activated ' + result.activated; }, 6000);
            </script>
            """, timeProvider: clock);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("true false", context.Document.Title);
        Assert.Empty(RequestsOf("https://example.com/report"));

        clock.Advance(TimeSpan.FromMilliseconds(4000));
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Empty(RequestsOf("https://example.com/report"));

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        var request = Assert.Single(RequestsOf("https://example.com/report"));
        Assert.Equal("POST", request.Method);
        Assert.Equal("data", System.Text.Encoding.UTF8.GetString(request.Body!.Value.Span));

        clock.Advance(TimeSpan.FromMilliseconds(1000));
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("activated true", context.Document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Request_IsSentWhenTheDocumentIsDestroyed_UnlessItIsAborted()
    {
        _loader.Resources["https://example.com/sent"] = new DocumentResource("");
        _loader.Resources["https://example.com/aborted"] = new DocumentResource("");
        var context = Load("""
            <script>
              fetchLater('/sent');
              const controller = new AbortController();
              fetchLater('/aborted', { signal: controller.signal });
              controller.abort();
            </script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Empty(RequestsOf("https://example.com/sent"));

        context.Navigate("https://example.com/next.html", _ => "<title>next</title>");
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Single(RequestsOf("https://example.com/sent"));
        Assert.Empty(RequestsOf("https://example.com/aborted"));
        Assert.Empty(_errors);
    }

    [Fact]
    public void Request_IsValidated()
    {
        var context = Load("""
            <script>
              const log = [];
              function attempt(name, callback) {
                try { callback(); log.push(name + ':ok'); } catch (e) { log.push(name + ':' + e.name); }
              }

              attempt('none', () => fetchLater());
              attempt('http', () => fetchLater('http://example.org/'));
              attempt('localhost', () => fetchLater('http://localhost/'));
              attempt('data', () => fetchLater('data:text/plain,a'));
              attempt('negative', () => fetchLater('/', { activateAfter: -1 }));
              attempt('stream', () => fetchLater('/', { method: 'POST', body: new ReadableStream(), duplex: 'half' }));
              const aborted = new AbortController();
              aborted.abort();
              attempt('aborted', () => fetchLater('/', { signal: aborted.signal }));

              // The quota of the requests to an origin is 64 kibibytes: the URL, the headers and the body
              let error;
              try { fetchLater('https://example.org/', { method: 'POST', body: 'x'.repeat(65536), referrer: '' }); } catch (e) { error = e; }
              log.push(error.name + ' ' + error.quota + ' ' + (error.requested > 65536) + ' ' + (error instanceof DOMException));
              document.title = log.join(' ');
            </script>
            """);

        Assert.Equal("none:TypeError http:TypeError localhost:ok data:TypeError negative:RangeError stream:TypeError aborted:AbortError QuotaExceededError 65536 true true", context.Document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Quota_IsDelegatedToTheCrossOriginFramesByThePermissionsPolicy()
    {
        const string Script = """
            <script>
              function attempt(size) {
                try { fetchLater('https://report.example/', { method: 'POST', body: 'x'.repeat(size), referrer: '' }); return 'ok'; } catch (e) { return e.name; }
              }

              // 8 kibibytes by default (deferred-fetch-minimal), 64 with allow="deferred-fetch"
              parent.postMessage(name + ':' + attempt(4000) + ',' + attempt(30000), '*');
            </script>
            """;
        _loader.Resources["https://other.example/minimal.html"] = new DocumentResource(Script);
        _loader.Resources["https://other.example/normal.html"] = new DocumentResource(Script);
        var context = Load("""
            <script>
              const log = [];
              onmessage = e => { log.push(e.data); log.sort(); document.title = log.join(' '); };
            </script>
            <iframe name="minimal" src="https://other.example/minimal.html"></iframe>
            <iframe name="normal" src="https://other.example/normal.html" allow="deferred-fetch"></iframe>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("minimal:ok,QuotaExceededError normal:ok,ok", context.Document.Title);
        Assert.Empty(_errors);
    }
}
