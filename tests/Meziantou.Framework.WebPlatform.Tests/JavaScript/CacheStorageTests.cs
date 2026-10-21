using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The Cache API (https://w3c.github.io/ServiceWorker/#cache-objects).</summary>
public sealed class CacheStorageTests : IDisposable
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

    private Document Load(string html, CacheStorageBackend? backend = null, string url = "https://example.com/", WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            CacheStorage = backend,
            Features = features,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void PutMatchKeysDelete()
    {
        var document = Load("""
            <script>
              (async () => {
                const log = [];
                const cache = await caches.open('v1');
                await cache.put('https://example.com/a?x=1', new Response('hello', { headers: { 'Content-Type': 'text/plain' } }));
                const response = await cache.match('https://example.com/a?x=1');
                log.push(await response.text(), response.headers.get('content-type'));
                log.push(String(await cache.match('https://example.com/a')));
                log.push((await cache.match('https://example.com/a', { ignoreSearch: true })).status);
                log.push((await cache.keys()).map(r => r.url).join('|'));
                log.push((await caches.keys()).join('|'), await caches.has('v1'));
                log.push(await cache.delete('https://example.com/a?x=1'), (await cache.keys()).length);
                log.push(await caches.delete('v1'), await caches.has('v1'));
                document.title = log.join(',');
              })().catch(e => document.title = 'error:' + e);
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("hello,text/plain,undefined,200,https://example.com/a?x=1,v1,true,true,0,true,false", document.Title);
    }

    [Fact]
    public void Backend_Sqlite_PersistsTheCaches()
    {
        var path = Path.Combine(Path.GetTempPath(), "caches-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var backend = new Storage.SqliteCacheStorageBackend(path))
            {
                var document = Load("""
                    <script>
                      caches.open('v1')
                        .then(cache => cache.put(new Request('https://example.com/data', { headers: { 'Accept': 'application/json' } }), Response.json({ a: 1 }, { status: 201, headers: { 'Vary': 'Accept' } })))
                        .then(() => document.title = 'written', e => document.title = 'error:' + e);
                    </script>
                    """, backend);
                Assert.Equal("written", document.Title);
            }

            using (var backend = new Storage.SqliteCacheStorageBackend(path))
            {
                var document = Load("""
                    <script>
                      (async () => {
                        const response = await caches.match(new Request('https://example.com/data', { headers: { 'Accept': 'application/json' } }));
                        const other = await caches.match(new Request('https://example.com/data', { headers: { 'Accept': 'text/html' } }));
                        document.title = [response.status, response.headers.get('content-type'), JSON.stringify(await response.json()), String(other), (await caches.keys()).join()].join(',');
                      })().catch(e => document.title = 'error:' + e);
                    </script>
                    """, backend);
                Assert.Equal("201,application/json,{\"a\":1},undefined,v1", document.Title);
            }

            Assert.Empty(_errors);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void DeletedCache_KeepsItsEntriesForTheCacheObjects()
    {
        var document = Load("""
            <script>
              (async () => {
                const cache = await caches.open('v1');
                await cache.put('https://example.com/a', new Response('a'));
                await caches.delete('v1');
                await cache.put('https://example.com/b', new Response('b'));
                const reopened = await caches.open('v1');
                document.title = [(await cache.keys()).length, (await reopened.keys()).length].join(',');
              })().catch(e => document.title = 'error:' + e);
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("2,0", document.Title);
    }

    [Fact]
    public void Caches_AreOnlyExposedInTheSecureContexts()
    {
        Assert.Equal("undefined,undefined", Load("<script>document.title = typeof caches + ',' + typeof Cache;</script>", url: "http://example.com/").Title);
        Assert.Equal("undefined,undefined", Load("<script>document.title = typeof caches + ',' + typeof Cache;</script>", features: WebPlatformFeatures.Default with { CacheStorageEnabled = false }).Title);
        Assert.Equal("object,function", Load("<script>document.title = typeof caches + ',' + typeof Cache;</script>").Title);
    }
}
