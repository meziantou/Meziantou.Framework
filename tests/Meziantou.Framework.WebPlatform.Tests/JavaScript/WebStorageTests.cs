using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>Web Storage (https://html.spec.whatwg.org/multipage/webstorage.html).</summary>
public sealed class WebStorageTests : IDisposable
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

    private Document Load(string html, StorageShed? localStorage = null, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            LocalStorage = localStorage,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void StorageShed_SetItem_IsVisibleToTheDocumentsOfTheOrigin()
    {
        var shed = new StorageShed(quota: 10);
        Assert.True(shed.SetItem("https://example.com", "key", "value"));
        Assert.False(shed.SetItem("https://example.com", "big", "a value that exceeds the quota"));

        var document = Load("<script>document.title = localStorage.getItem('key') + ',' + localStorage.length;</script>", shed);

        Assert.Equal("value,1", document.Title);
        Assert.Equal([new KeyValuePair<string, string>("key", "value")], shed.GetItems("https://example.com"));
        Assert.Empty(_errors);
    }

    [Fact]
    public void LocalStorage_MethodsAndNamedProperties()
    {
        var document = Load("""
            <script>
              localStorage.setItem('a', 1);
              localStorage.b = 'two';
              const results = [localStorage.length, localStorage.getItem('a'), localStorage.a, localStorage.getItem('b'), localStorage.key(1), localStorage.getItem('missing'), 'a' in localStorage, Object.keys(localStorage).join('|')];
              delete localStorage.a;
              localStorage.removeItem('b');
              results.push(localStorage.length, localStorage.a, localStorage.key(0));
              localStorage.setItem = 'shadowed';
              results.push(localStorage.length, localStorage.getItem('setItem'), Object.getPrototypeOf(localStorage) === Storage.prototype);
              document.title = results.join();
            </script>
            """);

        Assert.Equal("2,1,1,two,b,,true,a|b,0,,,1,shadowed,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void LocalStorage_IsSharedByTheBrowsingContextsOfTheShed()
    {
        var shed = new StorageShed();
        Load("<script>localStorage.setItem('key', 'value');</script>", shed);
        var document = Load("<script>document.title = localStorage.getItem('key') + ',' + sessionStorage.getItem('key');</script>", shed);

        Assert.Equal("value,null", document.Title);
        Assert.Equal([new KeyValuePair<string, string>("key", "value")], shed.GetItems("https://example.com"));
        Assert.Empty(shed.GetItems("https://other.example"));
    }

    [Fact]
    public void LocalStorage_OtherOrigin_HasItsOwnStorageArea()
    {
        var shed = new StorageShed();
        Load("<script>localStorage.setItem('key', 'value');</script>", shed);
        var document = Load("<script>document.title = String(localStorage.getItem('key'));</script>", shed, "https://other.example/");

        Assert.Equal("null", document.Title);
    }

    [Fact]
    public void SetItem_FiresStorageEventsAtTheOtherWindowsOfTheSameOrigin()
    {
        var document = Load("""
            <iframe srcdoc="<script>addEventListener('storage', e => parent.document.title += [e.key, e.oldValue, e.newValue, e.url, e.storageArea === localStorage, e.isTrusted].join() + ';');</script>"></iframe>
            <script>
              addEventListener('storage', () => document.title += 'unexpected;');
              onload = () => {
                localStorage.setItem('k', 'v1');
                localStorage.setItem('k', 'v1');
                localStorage.setItem('k', 'v2');
                localStorage.clear();
              };
            </script>
            """);

        Assert.Equal("k,,v1,https://example.com/,true,true;k,v1,v2,https://example.com/,true,true;,,,https://example.com/,true,true;", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void SessionStorage_IsCopiedToThePopupsWithAnOpener()
    {
        var document = Load("""
            <script>
              sessionStorage.setItem('before', '1');
              const popup = window.open();
              const noopener = window.open('', '_blank', 'noopener');
              sessionStorage.setItem('after', '2');
              popup.sessionStorage.setItem('popup', '3');
              document.title = [popup.sessionStorage.getItem('before'), popup.sessionStorage.getItem('after'), sessionStorage.getItem('popup'), noopener].join();
            </script>
            """);

        Assert.Equal("1,,,", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Storage_OpaqueOrigin_ThrowsSecurityError()
    {
        var document = Load("""
            <iframe sandbox="allow-scripts" srcdoc="<script>try { localStorage; } catch (e) { document.title = e.name; } try { sessionStorage; } catch (e) { document.title += ',' + e.name; }</script>"></iframe>
            """);

        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        Assert.Equal("SecurityError,SecurityError", iframe.ContentWindow!.Document.Title);
    }

    [Fact]
    public void SetItem_QuotaExceeded_ThrowsQuotaExceededError()
    {
        var document = Load("""
            <script>
              localStorage.setItem('k', '12345');
              try {
                localStorage.setItem('k2', '123456');
              } catch (e) {
                document.title = [e.name, e.code, e instanceof QuotaExceededError, e instanceof DOMException, e.quota, e.requested, localStorage.length].join();
              }
            </script>
            """, new StorageShed(quota: 10));

        Assert.Equal("QuotaExceededError,22,true,true,,,1", document.Title);
    }

    [Fact]
    public void QuotaExceededError_Constructor()
    {
        var document = Load("""
            <script>
              const e = new QuotaExceededError('message', { quota: 1, requested: 2 });
              let rangeError;
              try { new QuotaExceededError('', { quota: 2, requested: 1 }); } catch (ex) { rangeError = ex instanceof RangeError; }
              document.title = [e.name, e.message, e.code, e.quota, e.requested, new QuotaExceededError().quota, rangeError].join();
            </script>
            """);

        Assert.Equal("QuotaExceededError,message,22,1,2,,true", document.Title);
    }

    [Fact]
    public void Persistence_LoadsAndPersistsTheItems()
    {
        var persistence = new TestWebStoragePersistence();
        persistence.Add("https://example.com", "a", "1");
        persistence.Add("https://example.com", "b", "2");
        var document = Load("""
            <script>
              const results = [localStorage.length, localStorage.key(0), localStorage.getItem('a')];
              localStorage.setItem('a', 'one');
              localStorage.setItem('c', '3');
              localStorage.removeItem('b');
              document.title = results.join();
            </script>
            """, new StorageShed(persistence));

        Assert.Equal("2,a,1", document.Title);
        Assert.Equal(["load https://example.com", "set https://example.com a=one", "set https://example.com c=3", "remove https://example.com b"], persistence.Log);
        Assert.Equal([new KeyValuePair<string, string>("a", "one"), new KeyValuePair<string, string>("c", "3")], persistence.GetItems("https://example.com"));

        // Another shed (e.g. after a restart) loads the persisted items
        var other = new StorageShed(persistence);
        Assert.Equal([new KeyValuePair<string, string>("a", "one"), new KeyValuePair<string, string>("c", "3")], other.GetItems("https://example.com"));
        other.Clear();
        Assert.Empty(persistence.GetItems("https://example.com"));
        Assert.Equal("clear all", persistence.Log[^1]);
    }

    [Fact]
    public void Persistence_ClearPersistsTheChange()
    {
        var persistence = new TestWebStoragePersistence();
        persistence.Add("https://example.com", "a", "1");
        Load("<script>localStorage.clear(); localStorage.clear();</script>", new StorageShed(persistence));

        Assert.Equal(["load https://example.com", "clear https://example.com"], persistence.Log);
    }

    [Fact]
    public void Persistence_CredentiallessIframesAreNotPersisted()
    {
        var persistence = new TestWebStoragePersistence();
        var document = Load("""
            <iframe credentialless srcdoc="<script>localStorage.setItem('k', 'v'); parent.document.title = localStorage.getItem('k');</script>"></iframe>
            """, new StorageShed(persistence));

        Assert.Equal("v", document.Title);
        Assert.Empty(persistence.Log);
    }

    [Fact]
    public void Persistence_WriteFailure_ThrowsQuotaExceededError()
    {
        var persistence = new TestWebStoragePersistence { FailWrites = true };
        var document = Load("""
            <script>
              try { localStorage.setItem('k', 'v'); } catch (e) { document.title = [e.name, localStorage.length].join(); }
            </script>
            """, new StorageShed(persistence));

        Assert.Equal("QuotaExceededError,0", document.Title);
    }

    [Fact]
    public void Persistence_LoadFailure_ThrowsSecurityError()
    {
        var persistence = new TestWebStoragePersistence { FailLoads = true };
        var shed = new StorageShed(persistence);
        var document = Load("<script>try { localStorage; } catch (e) { document.title = e.name; }</script>", shed);
        Assert.Equal("SecurityError", document.Title);

        // The area is loaded again by the next document
        persistence.FailLoads = false;
        persistence.Add("https://example.com", "k", "v");
        document = Load("<script>document.title = localStorage.getItem('k');</script>", shed);
        Assert.Equal("v", document.Title);
    }

    [Fact]
    public void Persistence_LoadedItemsExceedingTheQuota_CannotGrow()
    {
        var persistence = new TestWebStoragePersistence();
        persistence.Add("https://example.com", "k", "0123456789");
        var document = Load("""
            <script>
              const results = [];
              try { localStorage.setItem('k2', 'v'); } catch (e) { results.push(e.name); }
              localStorage.setItem('k', '012345678');
              results.push(localStorage.getItem('k'));
              document.title = results.join();
            </script>
            """, new StorageShed(persistence, quota: 5));

        Assert.Equal("QuotaExceededError,012345678", document.Title);
    }

    private sealed class TestWebStoragePersistence : WebStoragePersistence
    {
        private readonly Dictionary<string, OrderedDictionary<string, string>> _areas = new(StringComparer.Ordinal);

        public List<string> Log { get; } = [];

        public bool FailLoads { get; set; }

        public bool FailWrites { get; set; }

        public void Add(string origin, string key, string value) => GetArea(origin)[key] = value;

        public List<KeyValuePair<string, string>> GetItems(string origin) => [.. GetArea(origin)];

        public override IEnumerable<KeyValuePair<string, string>> LoadItems(string origin)
        {
            if (FailLoads)
                throw new IOException("The items cannot be loaded");

            Log.Add("load " + origin);
            return GetItems(origin);
        }

        public override void SetItem(string origin, string key, string value)
        {
            if (FailWrites)
                throw new IOException("The item cannot be written");

            Log.Add($"set {origin} {key}={value}");
            GetArea(origin)[key] = value;
        }

        public override void RemoveItem(string origin, string key)
        {
            Log.Add($"remove {origin} {key}");
            GetArea(origin).Remove(key);
        }

        public override void ClearItems(string origin)
        {
            Log.Add("clear " + origin);
            GetArea(origin).Clear();
        }

        public override void ClearAll()
        {
            Log.Add("clear all");
            _areas.Clear();
        }

        private OrderedDictionary<string, string> GetArea(string origin)
        {
            if (!_areas.TryGetValue(origin, out var area))
            {
                area = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                _areas.Add(origin, area);
            }

            return area;
        }
    }

    [Fact]
    public void Documentation_LocalStorage()
    {
        #region WebPlatform_LocalStorage
        // The local storage areas of the origins, which several browsing contexts can share
        var storage = new StorageShed();
        using var context = new BrowsingContext(new BrowsingContextOptions { LocalStorage = storage });
        context.Navigate("https://example.com/", _ => "<p>Hello</p>");

        // window.localStorage, from .NET or from the scripts of the page
        context.Window.LocalStorage.SetItem("theme", "dark");
        Assert.Equal([new("theme", "dark")], storage.GetItems("https://example.com"));
        #endregion
    }
}
