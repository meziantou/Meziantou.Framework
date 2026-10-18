using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A JavaScript engine plugged into the browsing contexts (<see cref="BrowsingContextOptions.ScriptEngine"/>). The library
/// never executes scripts by itself: it implements the processing model of the HTML Standard (script elements, event
/// handlers, <c>javascript:</c> URLs, error reporting, the event loop) and calls the engine at the points the standard
/// defines, so the engine only runs code.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="Window"/> is the global object of a realm of the engine
/// (https://html.spec.whatwg.org/multipage/webappapis.html#realms-settings-objects-global-objects). The windows of the
/// browsing contexts that share the same options (a top-level browsing context and its nested browsing contexts) belong to
/// the same agent (https://html.spec.whatwg.org/multipage/webappapis.html#similar-origin-window-agent): the engine may
/// share the objects between their realms.
/// </para>
/// <para>
/// All the methods are called on the thread of the <see cref="DomEventLoop"/> of the browsing contexts. Each worker has its
/// own engine (<see cref="CreateWorkerScriptEngine"/>), used on the thread of the worker.
/// </para>
/// </remarks>
public abstract class ScriptEngine
{
    // ----------------------------------------------------------------------------------------------------------------
    // Realms
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates the realm whose global object is <paramref name="global"/>
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context). It is called when a
    /// new window is created for a browsing context, before its document becomes the active document, and when the script
    /// of a worker is fetched (on the thread of the worker, with the engine returned by <see cref="CreateWorkerScriptEngine"/>).
    /// An engine may create the realm lazily, when a script first runs in it.
    /// </summary>
    /// <param name="global">The global object of the realm: a <see cref="Window"/> or a <see cref="WorkerGlobalScope"/>.</param>
    protected internal abstract void CreateRealm(GlobalScope global);

    /// <summary>
    /// Creates the script engine of a new worker (https://html.spec.whatwg.org/multipage/workers.html#run-a-worker): each
    /// dedicated or shared worker is a new agent, whose event loop runs on its own thread, so it needs its own engine. The
    /// method is called on the thread of the worker, before <see cref="CreateRealm"/> is called with the
    /// <see cref="WorkerGlobalScope"/> of the worker; the engine is then only used on that thread, and is disposed (when it
    /// implements <see cref="IDisposable"/>) when the worker ends.
    /// </summary>
    /// <param name="terminationToken">
    /// Canceled when the worker is terminated (e.g. <c>worker.terminate()</c>): the engine should abort the running script,
    /// by throwing any exception (it is not reported).
    /// </param>
    /// <returns>The engine of the worker, or null when the workers do not run scripts. The default implementation returns null.</returns>
    protected internal virtual ScriptEngine? CreateWorkerScriptEngine(CancellationToken terminationToken) => null;

    /// <summary>
    /// Called when a document becomes the active document of its browsing context, before it is parsed. The window of the
    /// document may be a new window (see <see cref="CreateRealm"/>), or the window of the initial <c>about:blank</c>
    /// document, which is reused by a same-origin document.
    /// </summary>
    /// <param name="document">The new active document.</param>
    protected internal virtual void OnDocumentActivated(Document document)
    {
    }

    /// <summary>
    /// Called when the browsing context of a window is discarded (e.g. its <c>iframe</c> is removed from the document, or it
    /// navigates to another document), or when a worker ends: the realm no longer runs scripts, and the engine may release it.
    /// </summary>
    /// <param name="global">The global object of the realm.</param>
    protected internal virtual void DestroyRealm(GlobalScope global)
    {
    }

    /// <summary>
    /// Called when the document-tree child navigables of the document of a window change (an <c>iframe</c> is inserted or
    /// removed): the indexed properties of the <c>WindowProxy</c> (<c>window[0]</c>) changed
    /// (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getownproperty).
    /// </summary>
    /// <param name="window">The window whose child navigables changed.</param>
    protected internal virtual void OnChildNavigablesChanged(Window window)
    {
    }

    /// <summary>
    /// Called when the trusted type policy factory of a window or of a worker is created (its <c>trustedTypes</c> attribute is first used):
    /// a <see cref="TrustedScript"/> may then be given to <c>eval()</c>, whose code is its data
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#hostgetcodeforeval(argument)), even without content security policy.
    /// </summary>
    /// <param name="global">The window or the worker global scope.</param>
    internal virtual void OnTrustedTypePolicyFactoryCreated(GlobalScope global)
    {
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scripts
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a classic script (https://html.spec.whatwg.org/multipage/webappapis.html#run-a-classic-script) in the realm of
    /// <see cref="ClassicScript.Global"/>. The library owns the ordering of the scripts and the "clean up after running
    /// script" steps: the engine only evaluates the code.
    /// </summary>
    /// <param name="script">The script to run.</param>
    /// <returns>
    /// The completion value of the script: a <see cref="string"/> when the value is a JavaScript string (the result of a
    /// <c>javascript:</c> URL is a string), otherwise any value (the library ignores it).
    /// </returns>
    /// <exception cref="Exception">
    /// Any exception thrown when the script cannot be parsed or when its evaluation throws (an abrupt completion). The
    /// library reports it (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception), using
    /// <see cref="ExtractErrorInformation"/>.
    /// </exception>
    protected internal abstract object? ExecuteClassicScript(ClassicScript script);

    /// <summary>
    /// Parses a classic script (https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-classic-script) without
    /// running it: returns the error to rethrow of a script that cannot be parsed (e.g. the <c>SyntaxError</c> of a worker
    /// script, which fires an <c>error</c> event at the <see cref="Worker"/> object instead of being reported), or null.
    /// </summary>
    /// <param name="script">The script, run afterwards by <see cref="ExecuteClassicScript"/> when it can be parsed.</param>
    /// <returns>The parse error (a value of the engine), or null. The default implementation returns null: the parse errors are thrown by <see cref="ExecuteClassicScript"/>.</returns>
    protected internal virtual object? ParseClassicScript(ClassicScript script) => null;

    // ----------------------------------------------------------------------------------------------------------------
    // Event handlers
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Compiles the value of an event handler content attribute (e.g. <c>onclick="..."</c>), the first time the event
    /// handler is used (https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler).
    /// </summary>
    /// <param name="source">The body of the function, its parameters and its scopes.</param>
    /// <returns>The function.</returns>
    /// <exception cref="Exception">Any exception (e.g. a <c>SyntaxError</c>): the library reports it and the event handler is null.</exception>
    protected internal abstract EventHandlerCallback CompileEventHandler(EventHandlerSource source);

    // ----------------------------------------------------------------------------------------------------------------
    // Event loop
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Queues a microtask of the library (e.g. a mutation observer notification) in the job queue of the engine, so that
    /// the microtasks of the library and the jobs of the engine (promise reactions) run in order
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#queue-a-microtask).
    /// </summary>
    /// <param name="microtask">The microtask. The engine runs it when it performs a microtask checkpoint.</param>
    protected internal abstract void QueueMicrotask(Action microtask);

    /// <summary>
    /// Runs the jobs of the engine and the microtasks queued with <see cref="QueueMicrotask"/> until the queue is empty
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#perform-a-microtask-checkpoint). The exceptions of the jobs are
    /// reported by the engine.
    /// </summary>
    protected internal abstract void PerformMicrotaskCheckpoint();

    /// <summary>
    /// Enters a separate microtask queue: until the returned scope is disposed, the jobs of the engine and the microtasks
    /// queued with <see cref="QueueMicrotask"/> go to a new queue, which <see cref="PerformMicrotaskCheckpoint"/> runs; the
    /// microtasks that were pending are kept, and are not run in the scope. The scopes nest. A script is considered running in
    /// the scope (<see cref="IsScriptRunning"/>): the callbacks invoked in it perform no microtask checkpoint.
    /// </summary>
    /// <remarks>
    /// The callbacks of a worklet are specified to run in another agent, with its own microtask queue: the layout of a
    /// <c>display: layout()</c> box runs the microtasks of its layout worklet until the promise of its callback is settled
    /// (https://drafts.css-houdini.org/css-layout-api-1/#run-a-work-queue), possibly while a script of the document reads a
    /// geometry, and the microtasks of that script must not run. The default implementation returns a scope that does
    /// nothing: the microtasks of the document then run with the ones of the worklet.
    /// </remarks>
    /// <returns>The scope, which restores the previous microtask queue when it is disposed.</returns>
    protected internal virtual IDisposable EnterNestedMicrotaskQueue() => EmptyScope.Instance;

    /// <summary>
    /// Gets whether a script is running, i.e. whether the JavaScript execution context stack is not empty. The library
    /// performs a microtask checkpoint after running a script or a callback only when no script is running
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#clean-up-after-running-script).
    /// </summary>
    protected internal abstract bool IsScriptRunning { get; }

    /// <summary>
    /// Gets whether a promise has a handler (its [[PromiseIsHandled]] internal slot). The promises rejected without handler
    /// are given to <see cref="GlobalScope.TrackPromiseRejection"/> by the engine.
    /// </summary>
    /// <param name="promise">A promise given to <see cref="GlobalScope.TrackPromiseRejection"/>.</param>
    /// <returns>Whether the promise is handled. The default implementation returns false.</returns>
    protected internal virtual bool IsPromiseHandled(object promise) => false;

    /// <summary>
    /// Runs the success steps once all the values are fulfilled promises, or the failure steps with the reason of the first
    /// rejected one (https://webidl.spec.whatwg.org/#wait-for-all). The values are values of the engine, resolved as promises
    /// of the realm of <paramref name="global"/>, or <see cref="RejectedPromise"/> for a promise rejected with an exception.
    /// </summary>
    /// <param name="global">The global object of the realm of the promises.</param>
    /// <param name="values">The values.</param>
    /// <param name="successSteps">The success steps, run by a promise reaction (or a microtask when there is no value).</param>
    /// <param name="failureSteps">The failure steps, given the rejection reason.</param>
    /// <remarks>The default implementation considers that the values are fulfilled promises: it queues a microtask to run the success steps.</remarks>
    protected internal virtual void WaitForAll(GlobalScope global, IReadOnlyList<object?> values, Action successSteps, Action<object?> failureSteps)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(failureSteps);
        var rejected = values.OfType<RejectedPromise>().FirstOrDefault();
        QueueMicrotask(rejected is null ? successSteps : () => failureSteps(rejected.Reason));
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#prepare-to-run-script: the JavaScript execution context stack is
    /// not empty until the returned scope is disposed, which cleans up after running script (a microtask checkpoint when the
    /// stack becomes empty). The callbacks invoked in the scope do not perform a microtask checkpoint.
    /// </summary>
    /// <param name="global">The global object of the settings object.</param>
    /// <returns>The scope. The default implementation returns a scope that does nothing.</returns>
    protected internal virtual IDisposable PrepareToRunScript(GlobalScope global) => EmptyScope.Instance;

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Errors
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Extracts the information of an exception (https://html.spec.whatwg.org/multipage/webappapis.html#extract-error):
    /// its message, the location of the script, and the value of the <c>error</c> attribute of the <see cref="ErrorEvent"/>.
    /// </summary>
    /// <param name="exception">
    /// A .NET <see cref="Exception"/> thrown by the engine or by the library, or a value of the engine (e.g. the reason of a
    /// rejected promise, or the argument of <c>reportError()</c>).
    /// </param>
    /// <returns>The error information. The default implementation uses the message of the .NET exceptions.</returns>
    protected internal virtual ScriptError ExtractErrorInformation(object? exception) => exception switch
    {
        Exception ex => new ScriptError(ex.Message, ex),
        null => new ScriptError("null", null),
        _ => new ScriptError(exception.ToString() ?? "", exception),
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Module scripts (https://html.spec.whatwg.org/multipage/webappapis.html#module-script)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the base URL of the active script (https://html.spec.whatwg.org/multipage/webappapis.html#active-script), or
    /// null when no script is running: the base URL of the strings compiled by <c>setTimeout()</c> and <c>setInterval()</c>.
    /// </summary>
    /// <returns>The base URL of the innermost running classic or module script, or null.</returns>
    protected internal virtual string? GetActiveScriptBaseUrl() => null;

    /// <summary>
    /// Returns the global object of the current realm (https://tc39.es/ecma262/#current-realm), or null when no script is
    /// running: the realm of the running function (e.g. the realm of a promise job, which has no entry settings object in
    /// the library).
    /// </summary>
    /// <returns>The global object of the current realm, or null.</returns>
    protected internal virtual GlobalScope? GetCurrentGlobal() => null;

    /// <summary>
    /// Returns the location of the code that is running (the source file, line and column of the innermost running script),
    /// or null when no script is running or when the engine cannot extract it: the source location of the violations of the
    /// content security policies (https://w3c.github.io/webappsec-csp/#create-violation-for-global, step 2).
    /// </summary>
    /// <returns>The location of the running code, or null.</returns>
    protected internal virtual ScriptLocation? GetCurrentScriptLocation() => null;

    /// <summary>
    /// Returns the fetch options of the active script (https://html.spec.whatwg.org/multipage/webappapis.html#active-script),
    /// or null when no script is running: the options of the modules imported by <c>import()</c> in a classic script.
    /// </summary>
    /// <returns>The fetch options of the innermost running classic or module script, or null.</returns>
    protected internal virtual ModuleFetchOptions? GetActiveScriptFetchOptions() => null;

    /// <summary>
    /// Gets whether the engine supports the module scripts (<c>&lt;script type="module"&gt;</c>, <c>import()</c>). When it
    /// is false (the default), the module scripts are not fetched nor run.
    /// </summary>
    protected internal virtual bool SupportsModuleScripts => false;

    /// <summary>
    /// Parses a module script and loads its descendants ("fetch the descendants of and link a module script",
    /// https://html.spec.whatwg.org/multipage/webappapis.html#fetch-the-descendants-of-and-link-a-module-script): the
    /// engine resolves the module specifiers and fetches the modules with the library. <paramref name="onComplete"/> is
    /// called once the graph is loaded, with true, or with false when a module of the graph cannot be fetched (the script
    /// element then fires an <c>error</c> event). A parse error is stored in <see cref="ModuleScript.ErrorToRethrow"/>.
    /// </summary>
    /// <param name="script">The root of the module graph.</param>
    /// <param name="onComplete">Called when the graph is loaded (possibly synchronously).</param>
    protected internal virtual void LoadModuleScriptGraph(ModuleScript script, Action<bool> onComplete)
        => throw new NotSupportedException("The script engine does not support the module scripts");

    /// <summary>
    /// Fetches the graph of an external module script ("fetch a module script graph",
    /// https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-script-tree): the root is fetched ("fetch a
    /// single module script"), then its descendants are loaded. <paramref name="onComplete"/> is called with the module
    /// script, or with null when a module of the graph cannot be fetched. A parse error is stored in
    /// <see cref="ModuleScript.ErrorToRethrow"/>.
    /// </summary>
    /// <param name="global">The global object of the realm in which the script runs.</param>
    /// <param name="url">The URL of the root.</param>
    /// <param name="options">The fetch options of the script.</param>
    /// <param name="element">The script element that fetches the script, or null.</param>
    /// <param name="onComplete">Called when the graph is loaded (possibly synchronously).</param>
    /// <remarks>
    /// The default implementation fetches the root with the library, then calls <see cref="LoadModuleScriptGraph"/>. An
    /// engine with a module map overrides it to reuse the module of the URL when it is already fetched (the module is
    /// evaluated once, and its parse error is remembered).
    /// </remarks>
    protected internal virtual void FetchModuleScriptGraph(GlobalScope global, string url, ModuleFetchOptions options, Element? element, Action<ModuleScript?> onComplete)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onComplete);
        ModuleFetching.Fetch(global, url, ModuleType.JavaScript, options, element, fetched =>
        {
            if (fetched is null)
            {
                onComplete(null);
                return;
            }

            var script = new ModuleScript(global, fetched.SourceText, fetched.Url) { Url = fetched.Url, Element = element, FetchOptions = options };
            try
            {
                LoadModuleScriptGraph(script, success => onComplete(success ? script : null));
            }
#pragma warning disable CA1031 // An exception thrown by the engine is a failure of the graph
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ScriptErrors.Report(ex, global);
                onComplete(null);
            }
        });
    }

    /// <summary>
    /// Fetches a module script without loading its descendants nor running it ("fetch a modulepreload module script graph",
    /// https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-modulepreload-module-script-graph): the module of a
    /// <c>&lt;link rel=modulepreload&gt;</c> element. <paramref name="onComplete"/> is called with true when the module is
    /// fetched (even when it has a parse error), or with false when it cannot be fetched.
    /// </summary>
    /// <param name="global">The global object of the realm of the module.</param>
    /// <param name="url">The URL of the module.</param>
    /// <param name="type">The type of the module, determined by the destination.</param>
    /// <param name="destination">The destination of the request of a JavaScript module (a script-like destination).</param>
    /// <param name="options">The fetch options of the module.</param>
    /// <param name="element">The link element that fetches the module, or null.</param>
    /// <param name="onComplete">Called when the module is fetched (possibly synchronously).</param>
    /// <remarks>
    /// The default implementation fetches the module with the library and ignores its response. An engine with a module map
    /// overrides it to add the module to the module map, so that the module scripts and the imports do not fetch it again.
    /// </remarks>
    protected internal virtual void PreloadModuleScript(GlobalScope global, string url, ModuleType type, RequestDestination destination, ModuleFetchOptions options, Element? element, Action<bool> onComplete)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onComplete);
        ModuleFetching.Fetch(global, url, type, options, element, fetched => onComplete(fetched is not null), destination, isPreload: true);
    }

    /// <summary>
    /// Runs a module script (https://html.spec.whatwg.org/multipage/webappapis.html#run-a-module-script): links and
    /// evaluates the module graph, and reports the error to rethrow or the evaluation errors (including the errors of a
    /// top-level await) to the global object.
    /// </summary>
    /// <param name="script">The module script, whose graph is loaded.</param>
    protected internal virtual void ExecuteModuleScript(ModuleScript script)
        => throw new NotSupportedException("The script engine does not support the module scripts");

    // ----------------------------------------------------------------------------------------------------------------
    // Structured serialization (https://html.spec.whatwg.org/multipage/structured-data.html)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Serializes a value (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserialize), e.g. the
    /// message of <see cref="BroadcastChannel.PostMessage"/>. The result is given to <see cref="StructuredDeserialize"/>,
    /// possibly several times and for other realms.
    /// </summary>
    /// <param name="global">The global object of the current realm.</param>
    /// <param name="value">
    /// The value: a value of the engine, a .NET platform object, or null for <c>undefined</c> (the value of an IDL
    /// <c>any</c> argument).
    /// </param>
    /// <returns>The serialized value.</returns>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the value cannot be serialized.</exception>
    /// <remarks>The default implementation serializes the .NET primitive values and the serializable platform objects.</remarks>
    protected internal virtual object? StructuredSerialize(GlobalScope global, object? value) => StructuredClone.Serialize(value);

    /// <summary>
    /// Serializes a value for storage (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeforstorage),
    /// e.g. the state of <see cref="History.PushState"/>: a <c>SharedArrayBuffer</c> throws a <c>DataCloneError</c>.
    /// </summary>
    /// <param name="global">The global object of the current realm.</param>
    /// <param name="value">The value (see <see cref="StructuredSerialize(GlobalScope, object?)"/>).</param>
    /// <returns>The serialized value, given to <see cref="StructuredDeserialize"/>.</returns>
    /// <remarks>The default implementation calls <see cref="StructuredSerialize(GlobalScope, object?)"/>.</remarks>
    protected internal virtual object? StructuredSerializeForStorage(GlobalScope global, object? value) => StructuredSerialize(global, value);

    /// <summary>
    /// Serializes a value and transfers the objects of <paramref name="transferList"/>
    /// (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer), e.g. for
    /// <see cref="GlobalScope.StructuredClone"/>: the transferred objects are detached.
    /// </summary>
    /// <param name="global">The global object of the current realm.</param>
    /// <param name="value">The value (see <see cref="StructuredSerialize(GlobalScope, object?)"/>).</param>
    /// <param name="transferList">The objects to transfer: values of the engine or .NET platform objects.</param>
    /// <returns>The serialized value, given to <see cref="StructuredDeserialize"/>.</returns>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the value cannot be serialized or an object cannot be transferred.</exception>
    /// <remarks>The default implementation does not support transferring objects.</remarks>
    protected internal virtual object? StructuredSerialize(GlobalScope global, object? value, IReadOnlyList<object> transferList)
    {
        ArgumentNullException.ThrowIfNull(transferList);
        if (transferList.Count > 0)
            throw new DomException("Transferring objects is not supported", "DataCloneError");

        return StructuredSerialize(global, value);
    }

    /// <summary>
    /// Serializes a value for another agent (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer),
    /// e.g. a message posted to a worker or to a <see cref="MessagePort"/>: the result is given to <see cref="StructuredDeserialize"/>
    /// of another engine instance, on another thread, so it must not reference the values of this engine. The objects of
    /// <paramref name="transferList"/> are transferred: the <c>ArrayBuffer</c> objects of the engine, and the
    /// <see cref="MessagePort"/> objects.
    /// </summary>
    /// <param name="global">The global object of the current realm.</param>
    /// <param name="value">The value (see <see cref="StructuredSerialize(GlobalScope, object?)"/>).</param>
    /// <param name="transferList">The objects to transfer.</param>
    /// <returns>The serialized value.</returns>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the value cannot be serialized or an object cannot be transferred.</exception>
    /// <remarks>The default implementation calls <see cref="StructuredSerialize(GlobalScope, object?, IReadOnlyList{object})"/>, whose default result does not depend on the engine.</remarks>
    protected internal virtual object? StructuredSerializeForAgent(GlobalScope global, object? value, IReadOnlyList<object> transferList) => StructuredSerialize(global, value, transferList);

    /// <summary>
    /// Deserializes a value serialized by <see cref="StructuredSerialize(GlobalScope, object?)"/> in the realm of <paramref name="global"/>
    /// (https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize): the result is a new value of the
    /// realm (e.g. the <see cref="MessageEvent.Data"/> of the message events).
    /// </summary>
    /// <param name="global">The global object of the target realm.</param>
    /// <param name="serialized">The value returned by <see cref="StructuredSerialize(GlobalScope, object?)"/>.</param>
    /// <returns>The deserialized value.</returns>
    protected internal virtual object? StructuredDeserialize(GlobalScope global, object? serialized) => StructuredClone.Deserialize(serialized);

    /// <summary>
    /// Returns the operations of IndexedDB on the values of the realm of <paramref name="global"/> (the keys, the key paths
    /// and the serialization for storage of the values), or null when the engine does not support them: the .NET values
    /// are then supported (the primitive values and the serializable platform objects).
    /// </summary>
    internal virtual IIndexedDBValueOperations? GetIndexedDBValueOperations(GlobalScope global) => null;

    /// <summary>
    /// Returns the operations of the service workers on the <c>Request</c> and <c>Response</c> objects of the realm of
    /// <paramref name="global"/> (the Fetch API is implemented in JavaScript), or null when the engine does not support them:
    /// the service workers then cannot answer the requests.
    /// </summary>
    internal virtual IServiceWorkerFetchOperations? GetServiceWorkerFetchOperations(GlobalScope global) => null;
}

/// <summary>The value undefined, returned by the members whose type is <c>any</c> and whose .NET null value is null.</summary>
public sealed class UndefinedValue
{
    private UndefinedValue()
    {
    }

    /// <summary>Gets the unique instance of the value undefined.</summary>
    public static UndefinedValue Instance { get; } = new();
}

/// <summary>
/// The value null, for the members whose type is <c>any</c> (whose .NET null value is <c>undefined</c>), e.g. the
/// <see cref="ErrorEvent.Error"/> of the error event of a worker.
/// </summary>
public sealed class NullValue
{
    private NullValue()
    {
    }

    /// <summary>Gets the unique instance of the value null.</summary>
    public static NullValue Instance { get; } = new();
}

/// <summary>A promise rejected with a reason (e.g. the exception thrown by a callback whose return type is a promise), given to <see cref="ScriptEngine.WaitForAll"/>.</summary>
/// <param name="Reason">The reason: a value of the engine, or an exception.</param>
public sealed record RejectedPromise(object? Reason);
