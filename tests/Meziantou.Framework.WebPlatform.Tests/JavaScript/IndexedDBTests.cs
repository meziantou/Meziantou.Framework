using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Storage;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>IndexedDB (https://w3c.github.io/IndexedDB/).</summary>
public sealed class IndexedDBTests : IDisposable
{
    private const string WriteDraftScript = """
        <script>
          (async () => {
            const drafts = await navigator.storageBuckets.open('drafts');
            const db = await new Promise(resolve => {
              const request = drafts.indexedDB.open('db');
              request.onupgradeneeded = () => request.result.createObjectStore('store');
              request.onsuccess = () => resolve(request.result);
            });
            const transaction = db.transaction('store', 'readwrite');
            transaction.objectStore('store').put('saved', 'key');
            transaction.oncomplete = () => { db.close(); document.title = 'written'; };
          })();
        </script>
        """;

    private const string ReadDraftScript = """
        <script>
          async function readDraft(bucket) {
            if ((await bucket.indexedDB.databases()).length === 0)
              return 'empty';

            return await new Promise(resolve => {
              const request = bucket.indexedDB.open('db');
              request.onsuccess = () => {
                const db = request.result;
                db.transaction('store').objectStore('store').get('key').onsuccess = e => { db.close(); resolve(e.target.result); };
              };
            });
          }
        </script>
        """;

    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, IndexedDBBackend? backend = null, string url = "https://example.com/", WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            IndexedDB = backend,
            Features = features,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void OpenUpgradePutGet()
    {
        var document = Load("""
            <script>
              const log = [];
              const request = indexedDB.open('db', 2);
              request.onupgradeneeded = e => {
                log.push('upgrade:' + e.oldVersion + '->' + e.newVersion);
                const db = request.result;
                const store = db.createObjectStore('store', { keyPath: 'id' });
                store.createIndex('by_name', 'name', { unique: true });
                store.put({ id: 1, name: 'a' });
                store.put({ id: 2, name: 'b' });
              };
              request.onsuccess = () => {
                const db = request.result;
                log.push('success:' + db.version + ':' + [...db.objectStoreNames].join('|'));
                const tx = db.transaction('store');
                const store = tx.objectStore('store');
                store.get(2).onsuccess = e => log.push('get:' + e.target.result.name);
                store.index('by_name').getKey('a').onsuccess = e => log.push('index:' + e.target.result);
                store.count().onsuccess = e => log.push('count:' + e.target.result);
                tx.oncomplete = () => { log.push('complete'); document.title = log.join(','); };
              };
              request.onerror = () => { document.title = 'error:' + request.error.name; };
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("upgrade:0->2,success:2:store,get:b,index:1,count:2,complete", document.Title);
    }

    [Fact]
    public void StorageBuckets_DeletingABucketOfACredentiallessIframeKeepsTheBucketOfThePage()
    {
        var document = Load("""
            <script>
              (async () => {
                const root = await (await navigator.storageBuckets.open('drafts')).getDirectory();
                await root.getFileHandle('top.txt', { create: true });
                window.onFrameDone = async frameResult => {
                  const kept = await root.getFileHandle('top.txt').then(() => 'kept', e => e.name);
                  document.title = kept + ',' + frameResult;
                };
                const iframe = document.createElement('iframe');
                iframe.setAttribute('credentialless', '');
                iframe.srcdoc = `<script>(async () => {
                  const root = await (await navigator.storageBuckets.open('drafts')).getDirectory();
                  await root.getFileHandle('frame.txt', { create: true });
                  await navigator.storageBuckets.delete('drafts');
                  const again = await (await navigator.storageBuckets.open('drafts')).getDirectory();
                  parent.onFrameDone(await again.getFileHandle('frame.txt').then(() => 'leftover', () => 'empty'));
                })();<\/script>`;
                document.body.appendChild(iframe);
              })();
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("kept,empty", document.Title);
    }

    [Fact]
    public void ClearSiteData_OfACredentiallessIframeKeepsTheDatabasesOfThePage()
    {
        _loader.Resources["https://example.com/clear"] = new DocumentResource("") { Headers = [new("Content-Type", "text/plain"), new("Clear-Site-Data", "\"storage\"")] };
        var document = Load("""
            <script>
              (async () => {
                await new Promise(resolve => { const request = indexedDB.open('page'); request.onsuccess = () => { request.result.close(); resolve(); }; });
                window.onFrameDone = async () => {
                  document.title = (await indexedDB.databases()).map(database => database.name).join() || 'none';
                };
                const iframe = document.createElement('iframe');
                iframe.setAttribute('credentialless', '');
                iframe.srcdoc = `<script>fetch('/clear').then(() => parent.onFrameDone());<\/script>`;
                document.body.appendChild(iframe);
              })();
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("page", document.Title);
    }

    [Fact]
    public void StorageBuckets_ARecreatedBucketDoesNotSeeTheDatabasesOfTheDeletedOne()
    {
        var document = Load("""
            <script>
              (async () => {
                const first = await navigator.storageBuckets.open('inbox');
                await new Promise(resolve => {
                  const request = first.indexedDB.open('db');
                  request.onupgradeneeded = () => request.result.createObjectStore('store');
                  request.onsuccess = () => resolve(request.result);
                });
                await navigator.storageBuckets.delete('inbox');
                const second = await navigator.storageBuckets.open('inbox');
                document.title = (await second.indexedDB.databases()).map(database => database.name).join() || 'empty';
              })();
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("empty", document.Title);
    }

    [Fact]
    public void StorageBuckets_BrowsingContextGroupsThatShareABackendSeeTheSameBucket()
    {
        var backend = new InMemoryIndexedDBBackend();
        var writer = Load(WriteDraftScript, backend);
        var reader = Load("""
            <script>
              (async () => {
                await navigator.storageBuckets.open('inbox');
                const drafts = await navigator.storageBuckets.open('drafts');
                document.title = await readDraft(drafts);
              })();
            </script>
            """ + ReadDraftScript, backend);

        Assert.Empty(_errors);
        Assert.Equal("written", writer.Title);
        Assert.Equal("saved", reader.Title);
    }

    [Fact]
    public void StorageBuckets_APersistentBackendKeepsTheBucketsWhenItIsOpenedAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "idb-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var backend = new SqliteIndexedDBBackend(directory))
            {
                Assert.Equal("written", Load(WriteDraftScript, backend).Title);
            }

            using (var backend = new SqliteIndexedDBBackend(directory))
            {
                var reader = Load("""
                    <script>
                      (async () => {
                        const names = await navigator.storageBuckets.keys();
                        const drafts = await navigator.storageBuckets.open('drafts');
                        document.title = names.join() + ',' + await readDraft(drafts);
                      })();
                    </script>
                    """ + ReadDraftScript, backend);

                Assert.Equal("drafts,saved", reader.Title);
            }

            using (var backend = new SqliteIndexedDBBackend(directory))
            {
                var deleter = Load("""
                    <script>
                      (async () => {
                        await navigator.storageBuckets.delete('drafts');
                        const names = await navigator.storageBuckets.keys();
                        const drafts = await navigator.storageBuckets.open('drafts');
                        document.title = (names.join() || 'none') + ',' + await readDraft(drafts);
                      })();
                    </script>
                    """ + ReadDraftScript, backend);

                Assert.Equal("none,empty", deleter.Title);
            }

            Assert.Empty(_errors);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void StorageBuckets_TheBlockedRequestOfARemovedIframeIsCanceled()
    {
        var document = Load("""
            <script>
              (async () => {
                const bucket = await navigator.storageBuckets.open('b');
                const db = await new Promise(resolve => { const request = bucket.indexedDB.open('db', 1); request.onsuccess = () => resolve(request.result); });
                const iframe = document.createElement('iframe');
                window.onBlocked = () => setTimeout(() => {
                  iframe.remove();
                  db.close();
                  const request = bucket.indexedDB.deleteDatabase('db');
                  request.onsuccess = () => document.title = 'deleted';
                  request.onerror = () => document.title = 'error';
                }, 0);
                iframe.srcdoc = `<script>(async () => {
                  const request = (await navigator.storageBuckets.open('b')).indexedDB.open('db', 2);
                  request.onblocked = () => parent.onBlocked();
                })();<\/script>`;
                document.body.appendChild(iframe);
              })();
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("deleted", document.Title);
    }

    [Fact]
    public void Key_AProxyOfAnArrayIsNotAValidKey()
    {
        var document = Load("""
            <script>
              document.title = [indexedDB.cmp([1], [1]), (() => { try { indexedDB.cmp(new Proxy([1], {}), [1]); return 'valid'; } catch (e) { return e.name; } })()].join();
            </script>
            """);

        Assert.Equal("0,DataError", document.Title);
    }

    [Fact]
    public void CreateIndex_IndexesTheExistingRecords()
    {
        var document = Load("""
            <script>
              const log = [];
              const request = indexedDB.open('db2', 1);
              request.onupgradeneeded = e => {
                const db = request.result;
                db.onerror = e => log.push('db.error:' + e.target.error.name + ':' + e.target.error.message);
                const store = db.createObjectStore('store');
                for (let i = 0; i < 5; i++) store.add('object_' + i, i);
                store.createIndex('index', '');
                store.index('index').get('object_4').onsuccess = e => log.push('get:' + e.target.result);
              };
              request.onsuccess = () => { document.title = log.join(','); };
              request.onerror = () => { document.title = log.join(',') + ' error:' + request.error.name; };
            </script>
            """);

        Assert.Equal("get:object_4", document.Title);
    }

    private const string WriteScript = """
        <script>
          const request = indexedDB.open('db', 1);
          request.onupgradeneeded = () => {
            const store = request.result.createObjectStore('store', { autoIncrement: true });
            store.createIndex('by_tag', 'tags', { multiEntry: true });
          };
          request.onsuccess = () => {
            const tx = request.result.transaction('store', 'readwrite');
            tx.objectStore('store').put({ name: 'a', tags: ['x', 'y'], date: new Date(0), bytes: new Uint8Array([1, 2]), blob: new Blob(['hi']) });
            tx.objectStore('store').put({ name: 'b', tags: ['y'] });
            tx.oncomplete = () => { request.result.close(); document.title = 'written'; };
          };
        </script>
        """;

    private const string ReadScript = """
        <script>
          const request = indexedDB.open('db');
          request.onsuccess = async () => {
            const db = request.result;
            const tx = db.transaction('store');
            const index = tx.objectStore('store').index('by_tag');
            const keys = await new Promise(resolve => index.getAllKeys('y').onsuccess = e => resolve(e.target.result));
            const value = await new Promise(resolve => tx.objectStore('store').get(1).onsuccess = e => resolve(e.target.result));
            const text = await value.blob.text();
            document.title = [db.version, keys.join('|'), value.name, value.date.getTime(), value.bytes.join('|'), text, (await indexedDB.databases()).map(d => d.name + ':' + d.version).join()].join(',');
          };
        </script>
        """;

    [Fact]
    public void Backend_IsSharedByTheBrowsingContexts()
    {
        var backend = new InMemoryIndexedDBBackend();
        Assert.Equal("written", Load(WriteScript, backend).Title);
        var document = Load(ReadScript, backend);

        Assert.Empty(_errors);
        Assert.Equal("1,1|2,a,0,1|2,hi,db:1", document.Title);
        Assert.Equal([new IndexedDBDatabaseInfo("db", 1)], backend.GetDatabases("https://example.com"));
        Assert.Empty(backend.GetDatabases("https://other.example"));
    }

    [Fact]
    public void Backend_Sqlite_PersistsTheDatabases()
    {
        var directory = Path.Combine(Path.GetTempPath(), "idb-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var backend = new Storage.SqliteIndexedDBBackend(directory))
            {
                Assert.Equal("written", Load(WriteScript, backend).Title);
            }

            using (var backend = new Storage.SqliteIndexedDBBackend(directory))
            {
                var document = Load(ReadScript, backend);
                Assert.Equal("1,1|2,a,0,1|2,hi,db:1", document.Title);
            }

            Assert.Empty(_errors);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Transaction_Abort_RevertsTheChanges()
    {
        var document = Load("""
            <script>
              const request = indexedDB.open('db', 1);
              request.onupgradeneeded = () => request.result.createObjectStore('store');
              request.onsuccess = () => {
                const db = request.result;
                const tx = db.transaction('store', 'readwrite');
                tx.objectStore('store').put('value', 'key');
                tx.objectStore('store').put('other', 'key2').onsuccess = () => tx.abort();
                tx.onabort = () => {
                  db.transaction('store').objectStore('store').count().onsuccess = e => document.title = tx.error + ',' + e.target.result;
                };
              };
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("null,0", document.Title);
    }

    [Fact]
    public void Cursor_IteratesTheIndexRecords()
    {
        var document = Load("""
            <script>
              const request = indexedDB.open('db', 1);
              request.onupgradeneeded = () => {
                const store = request.result.createObjectStore('store', { keyPath: 'id' });
                store.createIndex('by_group', 'group');
                for (const [id, group] of [[1, 'b'], [2, 'a'], [3, 'b'], [4, 'c'], [5, 'a']]) store.put({ id, group });
              };
              request.onsuccess = () => {
                const log = [];
                const index = request.result.transaction('store').objectStore('store').index('by_group');
                index.openCursor(null, 'prevunique').onsuccess = e => {
                  const cursor = e.target.result;
                  if (cursor) { log.push(cursor.key + cursor.primaryKey); cursor.continue(); }
                  else document.title = log.join(',');
                };
              };
            </script>
            """);

        Assert.Empty(_errors);
        Assert.Equal("c4,b1,a2", document.Title);
    }

    [Fact]
    public void Feature_Disabled_HidesIndexedDB()
    {
        var document = Load("<script>document.title = typeof indexedDB + ',' + typeof IDBFactory + ',' + typeof IDBKeyRange;</script>", features: WebPlatformFeatures.Default with { IndexedDBEnabled = false });

        Assert.Equal("undefined,undefined,undefined", document.Title);
    }

    [Fact]
    public void OpaqueOrigin_ThrowsSecurityError()
    {
        var document = Load("<script>try { indexedDB.open('db'); } catch (e) { document.title = e.name; }</script>", url: "data:text/html,");

        Assert.Equal("SecurityError", document.Title);
    }
}
