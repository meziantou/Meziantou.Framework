using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A global object (https://html.spec.whatwg.org/multipage/webappapis.html#global-object): a <see cref="Window"/>, or the
/// global object of a worker (<see cref="WorkerGlobalScope"/>). It implements the members of the
/// <c>WindowOrWorkerGlobalScope</c> mixin (https://html.spec.whatwg.org/multipage/webappapis.html#windoworworkerglobalscope-mixin),
/// and the parts of its environment settings object the library needs (its event loop, its script engine, the report of
/// the exceptions and of the unhandled promise rejections).
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#windoworworkerglobalscope"/>
[IdlInterface("WindowOrWorkerGlobalScope", "https://html.spec.whatwg.org/multipage/webappapis.html#windoworworkerglobalscope")]
public abstract partial class GlobalScope : EventTarget
{
    // The global object of the worker whose event loop runs on the current thread, if any
    [ThreadStatic]
    private static GlobalScope? s_currentWorkerGlobal;

    private bool _inErrorReportingMode;
    private List<(object Promise, object? Reason)>? _aboutToBeNotifiedRejectedPromises;
    private ConditionalWeakTable<object, object?>? _outstandingRejectedPromises;
    private Performance? _performance;
    private List<WorkerAgent>? _workers;

    private protected GlobalScope(BrowsingContextOptions options, DomEventLoop eventLoop)
    {
        Options = options;
        EventLoop = eventLoop;
        TimeOriginTimestamp = eventLoop.TimeProvider.GetTimestamp();
        TimeOrigin = GetHighResolutionUnixTime(eventLoop.TimeProvider);
    }

    /// <summary>The current time in milliseconds since the Unix epoch, coarsened to 100 microseconds (https://w3c.github.io/hr-time/#dfn-coarsen-time).</summary>
    private static double GetHighResolutionUnixTime(TimeProvider timeProvider) => Math.Floor((timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds * 10) / 10;

    /// <summary>
    /// The current global object (https://html.spec.whatwg.org/multipage/webappapis.html#current-global-object): the global
    /// object of the worker whose event loop runs on the current thread, else the window of the current global document.
    /// </summary>
    internal static GlobalScope? Current => s_currentWorkerGlobal ?? Document.CurrentGlobalWindow;

    /// <summary>The global object of the worker whose event loop runs on the current thread (set by the thread of the worker).</summary>
    internal static GlobalScope? CurrentWorkerGlobal
    {
        get => s_currentWorkerGlobal;
        set => s_currentWorkerGlobal = value;
    }

    /// <summary>The options of the browsing contexts of the global object (for a worker, the options of the browsing contexts of its owner).</summary>
    internal BrowsingContextOptions Options { get; }

    /// <summary>The script engine of the realm of the global object, or null when scripting is disabled.</summary>
    internal abstract ScriptEngine? ScriptEngine { get; }

    /// <summary>The event loop of the global object: the event loop of the browsing contexts of a window, or the event loop of a worker.</summary>
    internal DomEventLoop EventLoop { get; }

    /// <summary>The time origin of the global object (https://w3c.github.io/hr-time/#dfn-time-origin), as a timestamp of <see cref="DomEventLoop.TimeProvider"/>.</summary>
    internal long TimeOriginTimestamp { get; private protected set; }

    /// <summary>The time origin of the global object, in milliseconds since the Unix epoch.</summary>
    internal double TimeOrigin { get; private protected set; }

    /// <summary>
    /// Sets the time origin of the global object to a timestamp of <see cref="DomEventLoop.TimeProvider"/>: the time origin of
    /// a window is the start of the navigation that created its document (https://w3c.github.io/hr-time/#dfn-time-origin).
    /// </summary>
    internal void SetTimeOrigin(long timestamp)
    {
        var timeProvider = EventLoop.TimeProvider;
        var elapsed = timeProvider.GetElapsedTime(timestamp, timeProvider.GetTimestamp());
        TimeOriginTimestamp = timestamp;
        TimeOrigin = Math.Floor((timeProvider.GetUtcNow() - elapsed - DateTimeOffset.UnixEpoch).TotalMilliseconds * 10) / 10;
    }

    /// <summary>The import map of the global object (https://html.spec.whatwg.org/multipage/webappapis.html#concept-global-import-map).</summary>
    internal ImportMap ImportMap { get; } = new();

    /// <summary>
    /// The API base URL of the settings object of the global object (https://html.spec.whatwg.org/multipage/webappapis.html#api-base-url):
    /// the base URL of the document of a window, or the URL of the script of a worker.
    /// </summary>
    internal abstract Urls.UrlRecord ApiBaseUrl { get; }

    /// <summary>The URL used as the file name of the scripts compiled by the global object (e.g. the strings of <c>setTimeout()</c>).</summary>
    internal abstract string ScriptUrl { get; }

    /// <summary>The origin of the settings object of the global object (https://html.spec.whatwg.org/multipage/webappapis.html#concept-settings-object-origin).</summary>
    internal abstract OriginRecord OriginRecord { get; }

    /// <summary>
    /// Whether the scripts can run in the realm of the global object ("check if we can run script",
    /// https://html.spec.whatwg.org/multipage/webappapis.html#check-if-we-can-run-script): the document of a window is
    /// fully active, a worker is not closing.
    /// </summary>
    internal abstract bool CanRunScript { get; }

    /// <summary>Whether scripting is enabled for the global object (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-script).</summary>
    internal abstract bool IsScriptingEnabled { get; }

    /// <summary>The kind of the global object, to check whether the interfaces and the members are exposed in its realm (https://webidl.spec.whatwg.org/#dfn-exposed).</summary>
    internal abstract ExposureSet Exposure { get; }

    /// <summary>The name of the IDL interface of the global object (e.g. <c>Window</c>, <c>DedicatedWorkerGlobalScope</c>).</summary>
    internal abstract string InterfaceName { get; }

    /// <summary>The associated document of a window, or null for a worker.</summary>
    internal virtual Document? AssociatedDocument => null;

    /// <summary>
    /// The document that represents the environment settings object of the global object for the algorithms that take a
    /// document (the client of the fetches, the origin, the policy container, the URL parsing...): the associated document
    /// of a window, or a document of a worker that is never exposed to the scripts.
    /// </summary>
    internal abstract Document EnvironmentDocument { get; }

    /// <summary>
    /// Queues a global task on the event loop of the global object
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#queue-a-global-task): the task runs only if the scripts can
    /// run in the realm of the global object.
    /// </summary>
    internal abstract void QueueGlobalTask(Action task);

    /// <summary>https://w3c.github.io/hr-time/#the-performance-attribute</summary>
    /// <seealso href="https://w3c.github.io/hr-time/#dom-windoworworkerglobalscope-performance"/>
    [IdlMember("performance", "https://w3c.github.io/hr-time/#dom-windoworworkerglobalscope-performance")]
    public Performance Performance => _performance ??= new Performance(this);

    /// <summary>The current high resolution time of the global object, in milliseconds relative to its time origin (https://w3c.github.io/hr-time/#dfn-current-high-resolution-time).</summary>
    internal double GetCurrentHighResolutionTime() => ToRelativeHighResolutionTime(EventLoop.TimeProvider.GetTimestamp());

    /// <summary>Converts a timestamp to milliseconds relative to the time origin, coarsened to 100 microseconds (https://w3c.github.io/hr-time/#dfn-coarsen-time).</summary>
    internal double ToRelativeHighResolutionTime(long timestamp)
    {
        var milliseconds = (timestamp - TimeOriginTimestamp) * 1000d / EventLoop.TimeProvider.TimestampFrequency;
        return Math.Floor(milliseconds * 10) / 10;
    }

    /// <summary>Registers a dedicated worker owned by the global object, terminated with it (<see cref="TerminateWorkers"/>).</summary>
    internal void AddWorker(WorkerAgent worker)
    {
        _workers ??= [];
        _workers.RemoveAll(static agent => agent.IsClosing);
        _workers.Add(worker);
    }

    /// <summary>Terminates the dedicated workers owned by the global object (e.g. when the document of a window is discarded).</summary>
    internal void TerminateWorkers()
    {
        if (_workers is not { } workers)
            return;

        _workers = null;
        foreach (var worker in workers)
        {
            worker.Terminate();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // WindowOrWorkerGlobalScope (https://html.spec.whatwg.org/multipage/webappapis.html#windoworworkerglobalscope-mixin)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the entry point of IndexedDB (https://w3c.github.io/IndexedDB/#dom-windoworworkerglobalscope-indexeddb): the
    /// databases of the origin, stored by <see cref="BrowsingContextOptions.IndexedDB"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-windoworworkerglobalscope-indexeddb"/>
    [IdlMember("indexedDB", "https://w3c.github.io/IndexedDB/#dom-windoworworkerglobalscope-indexeddb")]
    public IdbFactory IndexedDB => IndexedDBFactory ??= new IdbFactory(this);

    /// <summary>The <see cref="IndexedDB"/> factory, or null when it was not used.</summary>
    internal IdbFactory? IndexedDBFactory { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin")]
    public abstract string Origin { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext"/>
    [IdlMember("isSecureContext", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext")]
    public abstract bool IsSecureContext { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated"/>
    [IdlMember("crossOriginIsolated", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated")]
    public abstract bool CrossOriginIsolated { get; }

    /// <summary>
    /// An object that identifies the agent cluster of the agent of the global object
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#integration-with-the-javascript-agent-cluster-formalism): the
    /// <c>SharedArrayBuffer</c> objects can only be deserialized in the same agent cluster. Null when it is unknown (e.g. a
    /// window without browsing context).
    /// </summary>
    internal abstract object? AgentClusterKey { get; }

    /// <summary>
    /// Whether the cross-origin isolation mode of the agent cluster is not "none"
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#agent-cluster-cross-origin-isolation): the global object of
    /// a new realm has no <c>SharedArrayBuffer</c> property otherwise.
    /// </summary>
    internal abstract bool IsAgentClusterCrossOriginIsolated { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-btoa</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-btoa"/>
    [IdlMember("btoa", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-btoa")]
    public string Btoa(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] > 0xFF)
                throw new DomException("The string contains characters outside of the Latin1 range", "InvalidCharacterError");

            bytes[i] = (byte)data[i];
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-atob</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-atob"/>
    [IdlMember("atob", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-atob")]
    public string Atob(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = ForgivingBase64Decode(data) ?? throw new DomException("The string is not correctly encoded", "InvalidCharacterError");
        var result = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            result.Append((char)b);
        }

        return result.ToString();
    }

    /// <summary>https://infra.spec.whatwg.org/#forgiving-base64-decode</summary>
    internal static byte[]? ForgivingBase64Decode(string data)
    {
        var builder = new StringBuilder(data.Length);
        foreach (var c in data)
        {
            if (!Infra.InfraAscii.IsAsciiWhitespace(c))
            {
                builder.Append(c);
            }
        }

        if (builder.Length % 4 == 0 && builder.Length > 0)
        {
            if (builder[^1] == '=')
            {
                builder.Length--;
                if (builder[^1] == '=')
                {
                    builder.Length--;
                }
            }
        }

        if (builder.Length % 4 == 1)
            return null;

        var bits = 0;
        var bitCount = 0;
        var output = new List<byte>(builder.Length * 3 / 4);
        for (var i = 0; i < builder.Length; i++)
        {
            var c = builder[i];
            int value;
            if (c is >= 'A' and <= 'Z')
            {
                value = c - 'A';
            }
            else if (c is >= 'a' and <= 'z')
            {
                value = c - 'a' + 26;
            }
            else if (c is >= '0' and <= '9')
            {
                value = c - '0' + 52;
            }
            else if (c == '+')
            {
                value = 62;
            }
            else if (c == '/')
            {
                value = 63;
            }
            else
            {
                return null;
            }

            bits = (bits << 6) | value;
            bitCount += 6;
            if (bitCount >= 8)
            {
                bitCount -= 8;
                output.Add((byte)((bits >> bitCount) & 0xFF));
            }
        }

        return [.. output];
    }

    /// <summary>https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-queuemicrotask</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-queuemicrotask"/>
    [IdlMember("queueMicrotask", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-queuemicrotask")]
    public void QueueMicrotask(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        // https://wicg.github.io/scheduling-apis/#sec-patches-html-queue-a-microtask: the microtask has the continuation
        // state of the code that queued it (scheduler.yield() inherits the priority of the task)
        var eventLoop = EventLoop;
        var continuationState = eventLoop.CurrentContinuationState;
        eventLoop.QueueMicrotask(() =>
        {
            var previousContinuationState = eventLoop.CurrentContinuationState;
            eventLoop.CurrentContinuationState = continuationState;
            try
            {
                callback();
            }
#pragma warning disable CA1031 // Exceptions thrown by the callback are reported
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ScriptErrors.Report(ex, this);
            }
            finally
            {
                eventLoop.CurrentContinuationState = previousContinuationState;
            }
        });
    }

    /// <summary>The lock manager of the storage key of the global object (https://w3c.github.io/web-locks/#obtain-a-lock-manager), once it used <c>navigator.locks</c>.</summary>
    internal LockManagerState? LockManagerState { get; set; }

    // The open writable file streams and access handles of the global object (https://fs.spec.whatwg.org/), which hold a lock
    // on their file
    private HashSet<object>? _fileSystemResources;

    internal void RegisterFileSystemResource(object resource)
    {
        var resources = LazyInitializer.EnsureInitialized(ref _fileSystemResources);
        lock (resources)
        {
            resources.Add(resource);
        }
    }

    /// <summary>Whether the global object has open writable file streams or access handles.</summary>
    internal bool HasFileSystemResources
    {
        get
        {
            if (_fileSystemResources is not { } resources)
                return false;

            lock (resources)
            {
                return resources.Count > 0;
            }
        }
    }

    internal void UnregisterFileSystemResource(object resource)
    {
        if (_fileSystemResources is { } resources)
        {
            lock (resources)
            {
                resources.Remove(resource);
            }
        }
    }

    /// <summary>
    /// Releases the locks of the files that the global object writes: its document is unloaded or destroyed, or the worker
    /// ended. The buffers of its writable file streams are discarded, as when they are aborted.
    /// </summary>
    internal void ReleaseFileSystemResources()
    {
        if (_fileSystemResources is not { } resources)
            return;

        object[] snapshot;
        lock (resources)
        {
            snapshot = [.. resources];
            resources.Clear();
        }

        foreach (var resource in snapshot)
        {
            if (resource is FileSystemWritableFileStream stream)
            {
                stream.Release();
            }
            else if (resource is FileSystemSyncAccessHandle accessHandle)
            {
                accessHandle.Release();
            }
        }
    }

    private Scheduler? _scheduler;

    /// <summary>The permission statuses that have <c>change</c> listeners: they live as long as the global object.</summary>
    private HashSet<PermissionStatus>? _observedPermissionStatuses;

    /// <summary>Keeps a permission status alive while the global object is alive (see <see cref="PermissionStore"/>, which only references it weakly).</summary>
    internal void KeepPermissionStatusAlive(PermissionStatus status) => (_observedPermissionStatuses ??= []).Add(status);

    /// <summary>
    /// Gets the scheduler of the global object (https://wicg.github.io/scheduling-apis/#dom-windoworworkerglobalscope-scheduler),
    /// which posts prioritized tasks to its event loop.
    /// </summary>
    /// <seealso href="https://wicg.github.io/scheduling-apis/#dom-windoworworkerglobalscope-scheduler"/>
    [IdlMember("scheduler", "https://wicg.github.io/scheduling-apis/#dom-windoworworkerglobalscope-scheduler")]
    public Scheduler Scheduler => _scheduler ??= new Scheduler(this);

    /// <summary>
    /// Returns a structured clone of a value (https://html.spec.whatwg.org/multipage/structured-data.html#dom-structuredclone):
    /// the value is serialized and deserialized in the realm of the global object by the script engine (without script
    /// engine, only the .NET primitive values and the serializable platform objects can be cloned). The objects of the
    /// transfer list are transferred (detached) when there is a script engine.
    /// </summary>
    /// <param name="value">The value: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    /// <param name="options">The options: the objects to transfer.</param>
    /// <returns>The clone of the value.</returns>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the value cannot be cloned, or when an object of the transfer list cannot be transferred.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/structured-data.html#dom-structuredclone"/>
    [IdlMember("structuredClone", "https://html.spec.whatwg.org/multipage/structured-data.html#dom-structuredclone")]
    public object? StructuredClone(object? value, StructuredSerializeOptions? options = null)
    {
        var transfer = options?.Transfer ?? [];
        if (ScriptEngine is { } engine)
            return engine.StructuredDeserialize(this, engine.StructuredSerialize(this, value, transfer));

        if (transfer.Count > 0)
            throw new DomException("Transferring objects is not supported", "DataCloneError");

        return Scripting.StructuredClone.Deserialize(Scripting.StructuredClone.Serialize(value));
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#dom-reporterror: reports an exception, as if it was thrown by a
    /// script and not caught (an <see cref="ErrorEvent"/> is fired at the global object).
    /// </summary>
    /// <param name="e">The exception: a value of the script engine, or a .NET exception.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-reporterror"/>
    [IdlMember("reportError", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-reporterror")]
    public void ReportError(object? e)
    {
        var error = ScriptEngine?.ExtractErrorInformation(e) ?? (e is Exception exception ? new ScriptError(exception.Message, exception) : new ScriptError(e?.ToString() ?? "null", e));
        ReportException(error);
    }

    /// <summary>
    /// Reports an exception to this global object (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception):
    /// fires an <see cref="ErrorEvent"/> at the global object, then, when the event is not canceled (e.g. by <c>onerror</c>
    /// returning true), gives the error to <see cref="BrowsingContextOptions.UnhandledScriptError"/> (the error of a
    /// dedicated worker is first propagated to its <see cref="Worker"/> object). An exception reported while an error is
    /// being reported (e.g. thrown by an error event listener) does not fire another event.
    /// </summary>
    /// <param name="error">The information of the exception.</param>
    /// <returns>Whether the error was handled: the error event was canceled.</returns>
    [NotInIdl("The report an exception algorithm, used by the script engines")]
    public bool ReportException(ScriptError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var notHandled = true;
        if (!_inErrorReportingMode)
        {
            _inErrorReportingMode = true;
            try
            {
                var errorEvent = new ErrorEvent("error", new ErrorEventInit
                {
                    Cancelable = true,
                    Message = error.Message,
                    Filename = error.Filename,
                    Lineno = (uint)Math.Max(0, error.LineNumber),
                    Colno = (uint)Math.Max(0, error.ColumnNumber),
                    // The error of a muted error is null, not undefined (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception)
                    Error = error.Error ?? NullValue.Instance,
                });
                notHandled = FireEvent(errorEvent);
            }
            finally
            {
                _inErrorReportingMode = false;
            }
        }

        if (notHandled)
        {
            OnErrorNotHandled(error);
        }

        return !notHandled;
    }

    /// <summary>Called when the error event of a reported exception is not canceled: the error is given to the host, or propagated to the owner of a dedicated worker.</summary>
    private protected virtual void OnErrorNotHandled(ScriptError error) => ReportUnhandledError(error);

    /// <summary>Gives an error that was not handled to the host ("report exception to a developer console").</summary>
    internal void ReportUnhandledError(ScriptError error) => Options.UnhandledScriptError?.Invoke(this, error);

    /// <summary>Gives a message of the <c>console</c> namespace to the host (the Printer of https://console.spec.whatwg.org/#printer).</summary>
    internal void ReportConsoleMessage(ConsoleMessage message) => Options.ConsoleMessage?.Invoke(this, message);

    /// <summary>Whether the host receives the messages of the <c>console</c> namespace.</summary>
    internal bool HasConsoleMessageHandler => Options.ConsoleMessage is not null;

    // ----------------------------------------------------------------------------------------------------------------
    // Unhandled promise rejections (https://html.spec.whatwg.org/multipage/webappapis.html#unhandled-promise-rejections)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The HostPromiseRejectionTracker implementation of the HTML Standard
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#the-hostpromiserejectiontracker-implementation), called by
    /// the script engine when a promise of the realm of the global object is rejected without handler, or when a handler is
    /// added to such a promise. The <c>unhandledrejection</c> events are fired after the next microtask checkpoint, and the
    /// <c>rejectionhandled</c> events by a task.
    /// </summary>
    /// <param name="promise">The promise.</param>
    /// <param name="reason">The rejection reason of the promise (its [[PromiseResult]]).</param>
    /// <param name="operation">The operation.</param>
    [NotInIdl("The HostPromiseRejectionTracker implementation, used by the script engines")]
    public void TrackPromiseRejection(object promise, object? reason, PromiseRejectionOperation operation)
    {
        ArgumentNullException.ThrowIfNull(promise);
        if (operation == PromiseRejectionOperation.Reject)
        {
            if (_aboutToBeNotifiedRejectedPromises is null || _aboutToBeNotifiedRejectedPromises.Count == 0)
            {
                EventLoop.AddGlobalWithRejectedPromises(this);
            }

            (_aboutToBeNotifiedRejectedPromises ??= []).Add((promise, reason));
            return;
        }

        if (_aboutToBeNotifiedRejectedPromises is { } list)
        {
            var index = list.FindIndex(p => ReferenceEquals(p.Promise, promise));
            if (index >= 0)
            {
                list.RemoveAt(index);
                return;
            }
        }

        if (_outstandingRejectedPromises is null || !_outstandingRejectedPromises.Remove(promise))
            return;

        QueueGlobalTask(() => FireEvent(new PromiseRejectionEvent("rejectionhandled", new PromiseRejectionEventInit { Promise = promise, Reason = reason })));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#notify-about-rejected-promises</summary>
    internal void NotifyAboutRejectedPromises()
    {
        if (_aboutToBeNotifiedRejectedPromises is not { Count: > 0 } list)
            return;

        var promises = list.ToArray();
        list.Clear();
        QueueGlobalTask(() =>
        {
            foreach (var (promise, reason) in promises)
            {
                if (ScriptEngine?.IsPromiseHandled(promise) ?? false)
                    continue;

                var notCanceled = FireEvent(new PromiseRejectionEvent("unhandledrejection", new PromiseRejectionEventInit { Cancelable = true, Promise = promise, Reason = reason }));
                if (notCanceled)
                {
                    var error = ScriptEngine?.ExtractErrorInformation(reason) ?? new ScriptError(reason?.ToString() ?? "undefined", reason);
                    ReportUnhandledError(error);
                }

                if (!(ScriptEngine?.IsPromiseHandled(promise) ?? false))
                {
                    (_outstandingRejectedPromises ??= []).AddOrUpdate(promise, null);
                }
            }
        });
    }
}
