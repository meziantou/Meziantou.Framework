using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The Storage Standard (https://storage.spec.whatwg.org/) and the File System Standard (https://fs.spec.whatwg.org/).</summary>
public sealed class FileSystemTests : IDisposable
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

    private Document Load(string html, string url = "https://example.com/dir/page.html", bool scripting = true, FileSystemBackend? fileSystem = null, StorageOptions? storage = null, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = scripting ? _engine : null,
            DocumentLoader = _loader,
            FileSystem = fileSystem,
            Storage = storage,
            Features = features,
            UnhandledScriptError = (_, error) =>
            {
                lock (_errors)
                {
                    _errors.Add(error);
                }
            },
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private void AddResource(string url, string source, string contentType = "text/javascript")
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

    /// <summary>The promises of the file system are settled by tasks of the event loop.</summary>
    private async Task<T> Run<T>(Task<T> task)
    {
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        return await task;
    }

    private async Task Run(Task task)
    {
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        await task;
    }

    private static StorageManager Storage(Document document) => document.DefaultView!.Navigator.Storage;

    [Fact]
    public async Task DotNetApi_CreatesWritesAndReadsFiles()
    {
        var backend = new InMemoryFileSystemBackend();
        var document = Load("", scripting: false, fileSystem: backend);
        var root = await Run(Storage(document).GetDirectory());
        Assert.Equal("", root.Name);
        Assert.Equal(FileSystemHandleKind.Directory, root.Kind);

        var directory = await Run(root.GetDirectoryHandle("data", new FileSystemGetDirectoryOptions { Create = true }));
        var file = await Run(directory.GetFileHandle("file.txt", new FileSystemGetFileOptions { Create = true }));
        Assert.Equal(["data", "file.txt"], await Run(root.Resolve(file)));

        // The writes go to a buffer, which replaces the content of the file when the stream is closed
        var stream = await Run(file.CreateWritable());
        stream.Write("Hello world"u8);
        stream.Write("W"u8, position: 6);
        stream.Truncate(13);
        Assert.Equal(0ul, (await Run(file.GetFile())).Size);
        stream.Close();
        Assert.True(stream.IsClosed);
        var content = await Run(file.GetFile());
        Assert.Equal("Hello World\0\0", Encoding.UTF8.GetString(content.Data.Span));
        Assert.Equal("file.txt", content.Name);

        // The backend stores the entries of the origin
        Assert.Equal(FileSystemHandleKind.File, backend.GetEntryKind("https://example.com", ["data", "file.txt"]));
        Assert.Equal(13, backend.GetFileInfo("https://example.com", ["data", "file.txt"])!.Value.Size);
        Assert.True(backend.GetUsage("https://example.com") >= 13);

        var entries = new List<string>();
        await foreach (var (name, handle) in EnumerateAsync(directory))
        {
            entries.Add(name + ":" + handle.Kind);
        }

        Assert.Equal(["file.txt:File"], entries);

        var exception = await Assert.ThrowsAsync<DomException>(() => Run(root.RemoveEntry("data")));
        Assert.Equal("InvalidModificationError", exception.Name);
        await Run(root.RemoveEntry("data", new FileSystemRemoveOptions { Recursive = true }));
        Assert.Null(backend.GetEntryKind("https://example.com", ["data"]));
        exception = await Assert.ThrowsAsync<DomException>(() => Run(file.GetFile()));
        Assert.Equal("NotFoundError", exception.Name);
    }

    // The iteration settles its promises from tasks: the event loop runs between the entries
    private async IAsyncEnumerable<KeyValuePair<string, FileSystemHandle>> EnumerateAsync(FileSystemDirectoryHandle directory)
    {
        var enumerator = directory.GetEntriesAsync().GetAsyncEnumerator(TestContext.Current.CancellationToken);
        while (true)
        {
            var next = enumerator.MoveNextAsync().AsTask();
            await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
            if (!await next)
                break;

            yield return enumerator.Current;
        }
    }

    [Fact]
    public async Task DotNetApi_ValidatesTheNamesAndTheKinds()
    {
        var document = Load("", scripting: false);
        var root = await Run(Storage(document).GetDirectory());
        foreach (var name in (string[])["", ".", "..", "a/b", "a\\b"])
        {
            await Assert.ThrowsAsync<TypeErrorException>(() => Run(root.GetFileHandle(name, new FileSystemGetFileOptions { Create = true })));
            await Assert.ThrowsAsync<TypeErrorException>(() => Run(root.RemoveEntry(name)));
        }

        await Run(root.GetFileHandle("entry", new FileSystemGetFileOptions { Create = true }));
        Assert.Equal("TypeMismatchError", (await Assert.ThrowsAsync<DomException>(() => Run(root.GetDirectoryHandle("entry")))).Name);
        Assert.Equal("NotFoundError", (await Assert.ThrowsAsync<DomException>(() => Run(root.GetFileHandle("missing")))).Name);
        Assert.Equal("NotFoundError", (await Assert.ThrowsAsync<DomException>(() => Run(root.RemoveEntry("missing")))).Name);
    }

    [Fact]
    public async Task Locks_AWritableStreamAndAnAccessHandleExcludeEachOther()
    {
        var document = Load("", scripting: false);
        var root = await Run(Storage(document).GetDirectory());
        var file = await Run(root.GetFileHandle("file", new FileSystemGetFileOptions { Create = true }));

        // The writable streams share the lock of the file; an access handle needs it exclusively
        var first = await Run(file.CreateWritable());
        var second = await Run(file.CreateWritable());
        Assert.Equal("NoModificationAllowedError", (await Assert.ThrowsAsync<DomException>(() => Run(file.CreateSyncAccessHandle()))).Name);
        Assert.Equal("NoModificationAllowedError", (await Assert.ThrowsAsync<DomException>(() => Run(root.RemoveEntry("file")))).Name);
        first.Close();
        Assert.Equal("NoModificationAllowedError", (await Assert.ThrowsAsync<DomException>(() => Run(file.CreateSyncAccessHandle()))).Name);
        second.Abort();

        var accessHandle = await Run(file.CreateSyncAccessHandle());
        Assert.Equal("NoModificationAllowedError", (await Assert.ThrowsAsync<DomException>(() => Run(file.CreateWritable()))).Name);
        Assert.Equal("NoModificationAllowedError", (await Assert.ThrowsAsync<DomException>(() => Run(file.CreateSyncAccessHandle()))).Name);

        // The access handle reads and writes the file in place
        Assert.Equal(5, accessHandle.Write("12345"u8));
        Assert.Equal(2, accessHandle.Write("ab"u8, new FileSystemReadWriteOptions { At = 1 }));
        Assert.Equal(5ul, accessHandle.GetSize());
        var buffer = new byte[10];
        Assert.Equal(5, accessHandle.Read(buffer, new FileSystemReadWriteOptions { At = 0 }));
        Assert.Equal("1ab45", Encoding.UTF8.GetString(buffer, 0, 5));
        accessHandle.Truncate(2);
        accessHandle.Flush();
        accessHandle.Close();
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => accessHandle.GetSize()).Name);

        Assert.Equal("1a", Encoding.UTF8.GetString((await Run(file.GetFile())).Data.Span));
        await Run(root.RemoveEntry("file"));
    }

    [Fact]
    public async Task Locks_AreReleasedWhenTheDocumentIsUnloaded()
    {
        var backend = new InMemoryFileSystemBackend();
        var document = Load("", scripting: false, fileSystem: backend);
        var root = await Run(Storage(document).GetDirectory());
        var file = await Run(root.GetFileHandle("file", new FileSystemGetFileOptions { Create = true }));
        var accessHandle = await Run(file.CreateSyncAccessHandle());
        accessHandle.Write("data"u8);

        // Another browsing context that shares the backend cannot lock the file while the access handle is open
        var other = Load("", url: "https://example.com/other.html", scripting: false, fileSystem: backend);
        var otherRoot = await Run(Storage(other).GetDirectory());
        var otherFile = await Run(otherRoot.GetFileHandle("file"));
        Assert.Equal("NoModificationAllowedError", (await Assert.ThrowsAsync<DomException>(() => Run(otherFile.CreateWritable()))).Name);

        document.BrowsingContext!.Navigate("https://example.com/next.html", _ => "");
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        var stream = await Run(otherFile.CreateWritable(new FileSystemCreateWritableOptions { KeepExistingData = true }));
        stream.Write("D"u8);
        stream.Close();
        Assert.Equal("Data", Encoding.UTF8.GetString((await Run(otherFile.GetFile())).Data.Span));
    }

    [Fact]
    public async Task Quota_IsEnforcedByTheFileSystem()
    {
        var document = Load("", scripting: false, storage: new StorageOptions { Quota = 1000 });
        var root = await Run(Storage(document).GetDirectory());
        var file = await Run(root.GetFileHandle("file", new FileSystemGetFileOptions { Create = true }));
        var stream = await Run(file.CreateWritable());
        stream.Write(new byte[500]);
        Assert.Throws<QuotaExceededError>(() => stream.Write(new byte[1000]));
        Assert.Throws<QuotaExceededError>(() => stream.Truncate(2000));
        stream.Close();

        var estimate = await Run(Storage(document).Estimate());
        Assert.Equal(1000ul, estimate.Quota);
        Assert.True(estimate.Usage >= 500);
    }

    [Fact]
    public async Task StorageManager_PersistAsksTheHost()
    {
        var requests = new List<string>();
        var grant = false;
        var document = Load("", scripting: false, storage: new StorageOptions
        {
            RequestPersistentStorage = origin =>
            {
                requests.Add(origin);
                return grant;
            },
        });

        var storage = Storage(document);
        Assert.False(await Run(storage.Persisted()));
        Assert.False(await Run(storage.Persist()));
        grant = true;
        Assert.True(await Run(storage.Persist()));
        Assert.True(await Run(storage.Persisted()));

        // The host is not asked again once the storage is persistent
        Assert.True(await Run(storage.Persist()));
        Assert.Equal(["https://example.com", "https://example.com"], requests);
    }

    [Fact]
    public async Task Script_WritableStreamIsAWritableStream()
    {
        var document = Load("""
            <script>
            (async () => {
              const root = await navigator.storage.getDirectory();
              const handle = await root.getFileHandle('file.txt', { create: true });
              const stream = await handle.createWritable();
              const log = [stream instanceof WritableStream, stream instanceof FileSystemWritableFileStream, stream.locked];
              await stream.write('abc');
              await stream.write({ type: 'write', position: 5, data: new Uint8Array([100, 101]) });
              await stream.seek(1);
              await stream.write(new Blob(['XY']));
              const writer = stream.getWriter();
              await writer.write({ type: 'truncate', size: 6 });
              await writer.close();
              log.push(JSON.stringify(await (await handle.getFile()).text()));

              // The stream of a piped response
              const piped = await handle.createWritable({ keepExistingData: true });
              await new Blob(['pp']).stream().pipeTo(piped);
              log.push(JSON.stringify(await (await handle.getFile()).text()));

              const names = [];
              for await (const [name, entry] of root) names.push(name + ':' + entry.kind + ':' + (entry instanceof FileSystemFileHandle));
              log.push(names.join());
              log.push(Object.prototype.toString.call(root.keys()));
              try { await handle.createWritable().then(s => s.write({ type: 'seek' })); } catch (e) { log.push(e.name); }
              document.title = log.join(' ');
            })().catch(e => { document.title = 'error: ' + e; });
            </script>
            """);

        Assert.Equal("true true false \"aXY\\u0000\\u0000d\" \"ppY\\u0000\\u0000d\" file.txt:file:true [object FileSystemDirectoryHandle AsyncIterator] SyntaxError", await WaitForTitleAsync(document));
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Script_HandlesAreSerializable()
    {
        var document = Load("""
            <script>
            (async () => {
              const root = await navigator.storage.getDirectory();
              const handle = await root.getFileHandle('file.txt', { create: true });
              const clone = structuredClone({ handle, root });
              const log = [clone.handle !== handle, clone.handle instanceof FileSystemFileHandle, await clone.handle.isSameEntry(handle), await clone.root.isSameEntry(root)];

              // The handle stored in IndexedDB locates the same entry
              const db = await new Promise((resolve, reject) => {
                const request = indexedDB.open('handles');
                request.onupgradeneeded = () => request.result.createObjectStore('store');
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
              });
              const transaction = db.transaction('store', 'readwrite');
              transaction.objectStore('store').put(handle, 'key');
              const stored = await new Promise((resolve, reject) => {
                const request = transaction.objectStore('store').get('key');
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
              });
              log.push(stored.name, await stored.isSameEntry(handle));
              document.title = log.join(' ');
            })().catch(e => { document.title = 'error: ' + e; });
            </script>
            """);

        Assert.Equal("true true true true file.txt true", await WaitForTitleAsync(document));
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Script_AccessHandleInADedicatedWorker()
    {
        AddResource("https://example.com/dir/worker.js", """
            onmessage = async e => {
              try {
                const handle = e.data;
                const accessHandle = await handle.createSyncAccessHandle();
                const written = accessHandle.write(new TextEncoder().encode('worker data'), { at: 0 });
                const buffer = new Uint8Array(6);
                const read = accessHandle.read(buffer, { at: 0 });
                const size = accessHandle.getSize();
                accessHandle.flush();
                accessHandle.close();
                postMessage([written, read, new TextDecoder().decode(buffer), size, typeof navigator.storage.persist].join(' '));
              } catch (error) {
                postMessage('error: ' + error);
              }
            };
            """);
        var document = Load("""
            <script>
            (async () => {
              const root = await navigator.storage.getDirectory();
              const handle = await root.getFileHandle('file.txt', { create: true });
              const log = [typeof handle.createSyncAccessHandle];
              const worker = new Worker('worker.js');
              const result = await new Promise(resolve => { worker.onmessage = e => resolve(e.data); worker.postMessage(handle); });
              log.push(result, await (await handle.getFile()).text());
              document.title = log.join(' | ');
            })().catch(e => { document.title = 'error: ' + e; });
            </script>
            """);

        // createSyncAccessHandle() is exposed in the dedicated workers only, and persist() in the windows only
        Assert.Equal("undefined | 11 6 worker 11 undefined | worker data", await WaitForTitleAsync(document));
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Script_OpaqueOriginsHaveNoStorage()
    {
        var document = Load("""
            <iframe sandbox="allow-scripts" srcdoc="<script>
              (async () => {
                const log = [];
                for (const name of ['getDirectory', 'estimate', 'persisted', 'persist']) {
                  try { await navigator.storage[name](); log.push('ok'); } catch (e) { log.push(e.name); }
                }
                parent.postMessage(log.join(' '), '*');
              })();
            </script>"></iframe>
            <script>onmessage = e => { document.title = e.data; };</script>
            """);

        Assert.Equal("SecurityError TypeError TypeError TypeError", await WaitForTitleAsync(document));
    }
}
