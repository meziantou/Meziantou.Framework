using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>The Web Locks API (https://w3c.github.io/web-locks/).</summary>
public sealed class WebLocksTests : IDisposable
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

    private Document Load(string html, string url = "https://example.com/dir/page.html", bool scripting = true)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = scripting ? _engine : null,
            DocumentLoader = _loader,
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

    private static LockManager Locks(Document document) => document.DefaultView!.Navigator.Locks;

    [Fact]
    public async Task Request_HoldsTheLockUntilTheTaskOfTheCallbackCompletes()
    {
        var document = Load("<body>", scripting: false);
        var locks = Locks(document);
        var order = new List<string>();
        var release = new TaskCompletionSource<object?>();

        var first = locks.Request("resource", @lock =>
        {
            order.Add($"first {@lock!.Name} {@lock.Mode}");
            return release.Task;
        });
        var second = locks.Request("resource", @lock =>
        {
            order.Add("second");
            return Task.FromResult<object?>("result");
        });

        // The callbacks are called by tasks of the event loop
        Assert.Empty(order);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["first resource Exclusive"], order);
        Assert.False(first.IsCompleted);

        var snapshot = await locks.Query().WaitAsync(_contexts.EventLoop);
        var held = Assert.Single(snapshot.Held!);
        var pending = Assert.Single(snapshot.Pending!);
        Assert.Equal(("resource", LockMode.Exclusive), (held.Name, held.Mode));
        Assert.Equal(held.ClientId, pending.ClientId);
        Assert.False(string.IsNullOrEmpty(held.ClientId));

        release.SetResult(42);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(42, await first);
        Assert.Equal("result", await second);
        Assert.Equal(["first resource Exclusive", "second"], order);
        snapshot = await locks.Query().WaitAsync(_contexts.EventLoop);
        Assert.Empty(snapshot.Held!);
        Assert.Empty(snapshot.Pending!);
    }

    [Fact]
    public async Task Request_SharedLocksAreHeldTogether()
    {
        var document = Load("<body>", scripting: false);
        var locks = Locks(document);
        var order = new List<string>();
        var release = new TaskCompletionSource<object?>();
        var shared = new LockOptions { Mode = LockMode.Shared };

        var first = locks.Request("resource", shared, _ => { order.Add("shared 1"); return release.Task; });
        var second = locks.Request("resource", shared, _ => { order.Add("shared 2"); return release.Task; });
        var exclusive = locks.Request("resource", _ => { order.Add("exclusive"); return Task.FromResult<object?>(null); });
        var third = locks.Request("resource", shared, _ => { order.Add("shared 3"); return Task.FromResult<object?>(null); });
        _contexts.EventLoop.RunUntilIdle();

        // The shared request that follows the exclusive request waits for it: only the first request of a queue is grantable
        Assert.Equal(["shared 1", "shared 2"], order);
        release.SetResult(null);
        _contexts.EventLoop.RunUntilIdle();
        await Task.WhenAll(first, second, exclusive, third);
        Assert.Equal(["shared 1", "shared 2", "exclusive", "shared 3"], order);
    }

    [Fact]
    public async Task Request_IfAvailable_CallsTheCallbackWithNullWhenTheLockIsHeld()
    {
        var document = Load("<body>", scripting: false);
        var locks = Locks(document);
        var release = new TaskCompletionSource<object?>();
        _ = locks.Request("resource", _ => release.Task);

        var unavailable = locks.Request("resource", new LockOptions { IfAvailable = true }, @lock => Task.FromResult<object?>(@lock is null));
        var available = locks.Request("other", new LockOptions { IfAvailable = true }, @lock => Task.FromResult<object?>(@lock is null));
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(true, await unavailable);
        Assert.Equal(false, await available);
        release.SetResult(null);
    }

    [Fact]
    public async Task Request_Steal_ReleasesTheHeldLocksAndPrecedesThePendingRequests()
    {
        var document = Load("<body>", scripting: false);
        var locks = Locks(document);
        var order = new List<string>();
        var never = new TaskCompletionSource<object?>();

        var held = locks.Request("resource", _ => never.Task);
        var pending = locks.Request("resource", _ => { order.Add("pending"); return Task.FromResult<object?>(null); });
        _contexts.EventLoop.RunUntilIdle();
        var stolen = locks.Request("resource", new LockOptions { Steal = true }, _ => { order.Add("steal"); return Task.FromResult<object?>(null); });
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("AbortError", (await Assert.ThrowsAsync<DomException>(() => held)).Name);
        await Task.WhenAll(stolen, pending);
        Assert.Equal(["steal", "pending"], order);

        // The task of the stolen lock completes later: nothing is released twice
        never.SetResult(null);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty((await locks.Query().WaitAsync(_contexts.EventLoop)).Held!);
    }

    [Fact]
    public async Task Request_Signal_AbortsThePendingRequest()
    {
        var document = Load("<body>", scripting: false);
        var locks = Locks(document);
        var release = new TaskCompletionSource<object?>();
        var controller = new AbortController();
        var granted = new AbortController();
        var called = false;

        var first = locks.Request("resource", new LockOptions { Signal = granted.Signal }, _ => release.Task);
        var aborted = locks.Request("resource", new LockOptions { Signal = controller.Signal }, _ => { called = true; return Task.FromResult<object?>(null); });
        var next = locks.Request("resource", _ => Task.FromResult<object?>("next"));
        _contexts.EventLoop.RunUntilIdle();

        controller.Abort("reason");
        var exception = await Assert.ThrowsAsync<AbortSignalException>(() => aborted);
        Assert.Equal("reason", exception.Reason);

        // The signal is ignored once the lock is granted
        granted.Abort();
        release.SetResult("first");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("first", await first);
        Assert.Equal("next", await next);
        Assert.False(called);

        // A request that is aborted before its task runs does not call its callback, and releases the lock
        var late = new AbortController();
        var lateRequest = locks.Request("resource", new LockOptions { Signal = late.Signal }, _ => { called = true; return Task.FromResult<object?>(null); });
        late.Abort();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("AbortError", (await Assert.ThrowsAsync<DomException>(() => lateRequest)).Name);
        Assert.False(called);
        Assert.Empty((await locks.Query().WaitAsync(_contexts.EventLoop)).Held!);
    }

    [Fact]
    public async Task Request_InvalidArguments_Fail()
    {
        var document = Load("<body><iframe id=sandboxed sandbox='allow-scripts' srcdoc='<p>'></iframe>", scripting: false);
        var locks = Locks(document);
        static Task<object?> Callback(WebLock? @lock) => Task.FromResult<object?>(null);
        var signal = new AbortController().Signal;

        Assert.Equal("NotSupportedError", (await Assert.ThrowsAsync<DomException>(() => locks.Request("-reserved", Callback))).Name);
        Assert.Equal("NotSupportedError", (await Assert.ThrowsAsync<DomException>(() => locks.Request("a", new LockOptions { Steal = true, IfAvailable = true }, Callback))).Name);
        Assert.Equal("NotSupportedError", (await Assert.ThrowsAsync<DomException>(() => locks.Request("a", new LockOptions { Steal = true, Mode = LockMode.Shared }, Callback))).Name);
        Assert.Equal("NotSupportedError", (await Assert.ThrowsAsync<DomException>(() => locks.Request("a", new LockOptions { Steal = true, Signal = signal }, Callback))).Name);
        Assert.Equal("NotSupportedError", (await Assert.ThrowsAsync<DomException>(() => locks.Request("a", new LockOptions { IfAvailable = true, Signal = signal }, Callback))).Name);
        Assert.Equal("AbortError", (await Assert.ThrowsAsync<DomException>(() => locks.Request("a", new LockOptions { Signal = AbortSignal.Abort() }, Callback))).Name);

        // An opaque origin has no storage key
        var sandboxed = Locks(document.BrowsingContext!.Children[0].Document);
        Assert.Equal("SecurityError", (await Assert.ThrowsAsync<DomException>(() => sandboxed.Request("a", Callback))).Name);
        Assert.Equal("SecurityError", (await Assert.ThrowsAsync<DomException>(() => sandboxed.Query())).Name);
    }

    [Fact]
    public async Task Locks_AreSharedByTheSameOriginFramesAndReleasedWhenTheirDocumentIsUnloaded()
    {
        AddResource("https://example.com/frame.html", "<p>", "text/html");
        AddResource("https://other.example/frame.html", "<p>", "text/html");
        var document = Load("<body><iframe id=same src='https://example.com/frame.html'></iframe><iframe id=cross src='https://other.example/frame.html'></iframe>", scripting: false);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        var locks = Locks(document);
        var sameFrame = (HtmlIFrameElement)document.GetElementById("same")!;
        var sameLocks = Locks(document.BrowsingContext!.Children[0].Document);
        var crossLocks = Locks(document.BrowsingContext.Children[1].Document);
        var never = new TaskCompletionSource<object?>();

        var heldByFrame = sameLocks.Request("resource", _ => never.Task);
        var crossOrigin = crossLocks.Request("resource", _ => Task.FromResult<object?>("cross-origin"));
        var blocked = locks.Request("resource", _ => Task.FromResult<object?>("granted"));
        _contexts.EventLoop.RunUntilIdle();

        // The lock managers are per origin; the documents of an origin have different client ids
        Assert.Equal("cross-origin", await crossOrigin);
        Assert.False(blocked.IsCompleted);
        var snapshot = await locks.Query().WaitAsync(_contexts.EventLoop);
        Assert.NotEqual(Assert.Single(snapshot.Held!).ClientId, Assert.Single(snapshot.Pending!).ClientId);

        // The request of a document that is not fully active fails, and its locks were released
        sameFrame.Remove();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("granted", await blocked);
        Assert.False(heldByFrame.IsCompleted);
        Assert.Equal("InvalidStateError", (await Assert.ThrowsAsync<DomException>(() => sameLocks.Request("resource", _ => Task.FromResult<object?>(null)))).Name);
        Assert.Equal("InvalidStateError", (await Assert.ThrowsAsync<DomException>(() => sameLocks.Query())).Name);
    }

    [Fact]
    public async Task Request_FromScript_ResolvesWithTheResultOfTheCallback()
    {
        var document = Load("""
            <script>
              (async () => {
                const log = [];
                const lockManager = navigator.locks;
                log.push(lockManager instanceof LockManager, lockManager === navigator.locks);
                log.push(await lockManager.request('a', lock => `${lock instanceof Lock} ${lock.name} ${lock.mode}`));
                log.push(await lockManager.request('a', { mode: 'shared' }, async lock => lock.mode));
                log.push(await lockManager.request('a', async () => { throw new Error('failed'); }).catch(e => e.message));
                log.push(await lockManager.request('a', () => { throw new Error('sync'); }).catch(e => e.message));
                log.push(await lockManager.request('a').catch(e => e.name));
                log.push(await lockManager.request('a', { mode: 'other' }, () => {}).catch(e => e.name));
                log.push(await lockManager.request('-a', () => {}).catch(e => e.name));
                await lockManager.request('a', async () => {
                  log.push(await lockManager.request('a', { ifAvailable: true }, lock => lock));
                  const state = await lockManager.query();
                  log.push(JSON.stringify(state.held.map(l => [l.name, l.mode, typeof l.clientId])), state.pending.length);
                });
                document.title = log.join('|');
              })();
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("""true|true|true a exclusive|shared|failed|sync|TypeError|TypeError|NotSupportedError||[["a","exclusive","string"]]|0""", title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Locks_AreSharedWithTheWorkersAndReleasedWhenTheyAreTerminated()
    {
        AddResource("https://example.com/dir/worker.js", """
            let release;
            onmessage = e => {
              if (e.data === 'release') { release(); return; }
              navigator.locks.request(e.data, lock => {
                postMessage('held ' + lock.name);
                return new Promise(resolve => { release = resolve; });
              });
            };
            """);
        var document = Load("""
            <script>
              (async () => {
                const log = [];
                const message = worker => new Promise(resolve => { worker.onmessage = e => resolve(e.data); });
                const worker = new Worker('worker.js');
                worker.postMessage('a');
                log.push(await message(worker));

                // The lock is held by the worker (on its thread)
                log.push(await navigator.locks.request('a', { ifAvailable: true }, lock => lock === null));
                const state = await navigator.locks.query();
                log.push(state.held.length);
                const granted = navigator.locks.request('a', lock => 'granted after release');
                worker.postMessage('release');
                log.push(await granted);

                // A terminated worker releases its locks
                worker.postMessage('b');
                log.push(await message(worker));
                const afterTermination = navigator.locks.request('b', lock => 'granted after termination');
                worker.terminate();
                log.push(await afterTermination);
                document.title = log.join('|');
              })();
            </script>
            """);

        var title = await WaitForTitleAsync(document);
        Assert.Equal("held a|true|1|granted after release|held b|granted after termination", title);
        Assert.Empty(_errors);
    }
}

file static class TaskExtensions
{
    /// <summary>Runs the event loop until it is idle, then returns the result of the task.</summary>
    public static async Task<T> WaitAsync<T>(this Task<T> task, DomEventLoop eventLoop)
    {
        eventLoop.RunUntilIdle();
        return await task;
    }
}
