using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using static Meziantou.Framework.WebPlatform.JavaScript.JavaScriptValues;

namespace Meziantou.Framework.WebPlatform.JavaScript;

/// <summary>
/// A <see cref="ScriptEngine"/> implemented with Meziantou.Framework.JavaScriptEngine: set it as the
/// <see cref="BrowsingContextOptions.ScriptEngine"/> of a browsing context to run the scripts of its documents. The windows
/// of the browsing contexts share a <see cref="JsEngine"/> (the similar-origin window agent), and each window is the global
/// object of its own realm, created on first use. The DOM is exposed with its WebIDL bindings.
/// </summary>
/// <remarks>
/// The engine is not thread-safe: use it from the thread of the <see cref="DomEventLoop"/> of the browsing contexts. See
/// <see cref="JavaScriptScriptEngineOptions"/> for the limits of the resources the scripts can use.
/// </remarks>
#pragma warning disable CA1001 // The engine is disposed by Dispose
public partial class JavaScriptScriptEngine : ScriptEngine, IDisposable
#pragma warning restore CA1001
{
    // The scripts that can be cached for the process (e.g. the scripts of a test harness), by URL
    private static readonly ConcurrentDictionary<string, JsPreparedScript> PreparedScripts = new(StringComparer.Ordinal);

    private readonly ConditionalWeakTable<ClassicScript, JsPreparedScript> _parsedScripts = [];

    // The event loops of the realms, read from any thread (the jobs of the engine can be queued by other agents)
    private readonly ConditionalWeakTable<JsRealm, DomEventLoop> _eventLoops = [];
    private int _executionDepth;

    // The depth of the calls of the host (Evaluate, Call): the strings they compile are not checked by the content security policies
    private int _hostCodeDepth;

    /// <summary>Creates a script engine.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public JavaScriptScriptEngine(JavaScriptScriptEngineOptions? options = null)
        : this(options, CancellationToken.None)
    {
    }

    /// <summary>Creates the script engine of a worker (see <see cref="CreateWorkerScriptEngine"/>).</summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <param name="terminationToken">Canceled when the worker is terminated: the running script is aborted.</param>
    protected JavaScriptScriptEngine(JavaScriptScriptEngineOptions? options, CancellationToken terminationToken)
    {
        options ??= new JavaScriptScriptEngineOptions();
        Options = options;
        var host = new JavaScriptHost(this);
        Host = host;
        ModuleLoader = new JavaScriptModuleLoader(this);
        var engineOptions = new JsEngineOptions
        {
            Timeout = options.Timeout,
            MemoryLimit = options.MemoryLimit,
            MaxStatements = options.MaxStatements,
            RegExpTimeout = options.RegexTimeout,
            // The frames of the engine are not on the .NET stack: without a limit, an unbounded recursion would use all the
            // memory instead of throwing a RangeError, so null is the limit of the engine (as in the browsers)
            RecursionLimit = options.RecursionLimit ?? new JsEngineOptions().RecursionLimit,
            CancellationToken = terminationToken,
            TimeProvider = options.TimeProvider ?? TimeProvider.System,
        };
        options.ConfigureEngine?.Invoke(engineOptions);
        engineOptions.Host = host;
        engineOptions.ModuleLoader = ModuleLoader;
        Engine = new JsEngine(engineOptions);

        // The first realm is the realm of the first global object (the agent uses the current realm)
        Engine.CreateRealm();
        var state = new JavaScriptAdapterState(Engine, host);
        Agent = new WebIdlAgent<JavaScriptAdapter, object>(new JavaScriptAdapter(state), new WebIdlHost(this))
        {
            RealmFactory = state.CreateRealm,
            RealmCreated = realm =>
            {
                _eventLoops.AddOrUpdate((JsRealm)realm.EngineRealm, realm.GlobalScope.EventLoop);
                InitializeRealm(realm);
                OnRealmCreated(realm.GlobalScope, (JsObject)realm.Global);
            },
        };
    }

    /// <summary>Gets the event loop of the global object of a realm (from any thread), or null.</summary>
    internal DomEventLoop? GetEventLoop(JsRealm? realm) => realm is not null && _eventLoops.TryGetValue(realm, out var eventLoop) ? eventLoop : null;

    /// <summary>Gets the JavaScript engine (the agent of the windows).</summary>
    public JsEngine Engine { get; }

    /// <summary>Gets the options of the engine, also used by the engines of the workers.</summary>
    protected JavaScriptScriptEngineOptions Options { get; }

    internal JavaScriptHost Host { get; }

    internal WebIdlAgent<JavaScriptAdapter, object> Agent { get; }

    /// <summary>Creates the engine of a worker: a new engine with the same options (a subclass should override it to create an instance of its own type).</summary>
    protected internal override ScriptEngine? CreateWorkerScriptEngine(CancellationToken terminationToken) => new JavaScriptScriptEngine(Options, terminationToken);

    /// <summary>
    /// Called when the realm of a window is created, before a script runs in it: the host can add its own functions to the
    /// global object.
    /// </summary>
    /// <param name="global">The .NET global object: a <see cref="Window"/>, or the global object of a worker.</param>
    /// <param name="globalObject">The global object of the realm, whose prototype chain implements the interface of <paramref name="global"/>.</param>
    protected virtual void OnRealmCreated(GlobalScope global, JsObject globalObject)
    {
    }

    /// <summary>Initializes the global object of a new realm (used by the web-platform-tests runner).</summary>
    internal virtual void InitializeRealm(WebIdlRealm<JavaScriptAdapter, object> realm)
    {
    }

    /// <summary>
    /// Evaluates code in the realm of a window, as an indirect <c>eval</c>. The microtasks are not run: call
    /// <see cref="DomEventLoop.PerformMicrotaskCheckpoint"/> afterwards. As the code evaluated by the developer tools of a
    /// browser, the code and the strings it compiles while it runs (<c>eval()</c>, <c>Function()</c>) are not restricted by
    /// the content security policy of the document.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="code">The code.</param>
    /// <returns>The completion value.</returns>
    public JsValue Evaluate(Window window, string code)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(code);
        var realm = Agent.GetRealm(window);
        using var scope = EnterJavaScript();
        _hostCodeDepth++;
        try
        {
            return ToJs(realm.Run(() => realm.Evaluate(code)));
        }
        finally
        {
            _hostCodeDepth--;
        }
    }

    /// <summary>
    /// Runs a script in the realm of a window: a script parsed once with <see cref="JsPreparedScript.Parse(string, string?, int, int)"/>
    /// runs in several realms without being parsed again (e.g. a large library injected in each document). The microtasks are
    /// not run: call <see cref="DomEventLoop.PerformMicrotaskCheckpoint"/> afterwards.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="script">The parsed script.</param>
    /// <returns>The completion value of the script.</returns>
    public JsValue Evaluate(Window window, JsPreparedScript script)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(script);
        var realm = Agent.GetRealm(window);
        using var scope = EnterJavaScript();
        _hostCodeDepth++;
        try
        {
            return ToJs(realm.Run(() => ToObject(((JsRealm)realm.EngineRealm).Execute(script))));
        }
        finally
        {
            _hostCodeDepth--;
        }
    }

    /// <summary>
    /// Calls a function in the realm of a window, as a callback of the host (e.g. a function returned by <see cref="Evaluate(Window, string)"/>).
    /// The microtasks are not run: call <see cref="DomEventLoop.PerformMicrotaskCheckpoint"/> afterwards. The strings the
    /// function compiles while it runs (<c>eval()</c>, <c>Function()</c>) are not restricted by the content security policy of
    /// the document.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="function">The function.</param>
    /// <param name="thisValue">The <c>this</c> value of the call.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The value returned by the function.</returns>
    /// <exception cref="JsException">The function threw an exception.</exception>
    public JsValue Call(Window window, JsValue function, JsValue thisValue, params ReadOnlySpan<JsValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(window);
        var realm = Agent.GetRealm(window);
        using var scope = EnterJavaScript();
        var argumentArray = arguments.ToArray();
        _hostCodeDepth++;
        try
        {
            return ToJs(realm.Run(() => ToObject(Engine.Call(function, thisValue, argumentArray))));
        }
        finally
        {
            _hostCodeDepth--;
        }
    }

    /// <summary>
    /// Converts a .NET value to a value of the realm of a window, as the IDL type <c>any</c>: a platform object (e.g. a
    /// <see cref="Node"/>, an <see cref="Event"/>) is its wrapper object, as the scripts of the window see it; a string, a number
    /// and a boolean are the primitive values; null is <c>null</c>.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="value">The .NET value.</param>
    /// <returns>The value of the realm.</returns>
    public JsValue ToJsValue(Window window, object? value)
    {
        ArgumentNullException.ThrowIfNull(window);
        var realm = Agent.GetRealm(window);
        return ToJs(realm.ToJs(value));
    }

    /// <summary>
    /// Converts a value of the realm of a window to a .NET value, as the IDL type <c>any</c>: the wrapper object of a platform
    /// object is the .NET object (e.g. a <see cref="Node"/>), a string, a number and a boolean are the .NET values,
    /// <c>undefined</c> and <c>null</c> are null, and the other values (e.g. a plain object) are returned as they are.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="value">The value of the realm.</param>
    /// <returns>The .NET value.</returns>
    public object? FromJsValue(Window window, JsValue value)
    {
        ArgumentNullException.ThrowIfNull(window);
        var realm = Agent.GetRealm(window);
        return realm.ToAny(ToObject(value)) switch
        {
            UndefinedValue or NullValue => null,
            var any => any,
        };
    }

    /// <summary>Releases the engine.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the engine.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Agent.DisposeWebAssembly();
            Engine.Dispose();
        }
    }

    /// <summary>Whether the prepared (parsed) script can be cached for the process (e.g. the scripts of a test harness).</summary>
    internal virtual bool ShouldCacheScript(ClassicScript script) => false;

    // ----------------------------------------------------------------------------------------------------------------
    // Realms
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The realms are created on first use: most windows (e.g. the initial about:blank documents) never run scripts.</summary>
    protected internal override void CreateRealm(GlobalScope global)
    {
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scripts
    // ----------------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected internal override object? ExecuteClassicScript(ClassicScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var realm = Agent.GetRealm(script.Global);
        using var scope = EnterJavaScript();
        var hostDefined = new ScriptHostDefined { BaseUrl = script.BaseUrl, FetchOptions = script.FetchOptions, IsGlobalScript = script.Url == script.Global.ScriptUrl };
        ActiveScripts.Push(hostDefined);
        if (script.MutedErrors)
        {
            MutedScriptLocations.TryAdd(script.Url, value: true);
        }
        else
        {
            MutedScriptLocations.TryRemove(script.Url, out _);
        }

        try
        {
            var result = realm.Run(() =>
            {
                var engineRealm = (JsRealm)realm.EngineRealm;
                JsPreparedScript prepared;
                if (ShouldCacheScript(script))
                {
                    prepared = PreparedScripts.GetOrAdd(script.Url, static (_, source) => JsPreparedScript.TryParse(source.Script.SourceText, source.Script.Url, 0, 0, out var parsed, out var error) ? parsed : throw source.Engine.CreateSyntaxError(source.Realm, error), (Script: script, Engine: this, Realm: realm));
                }
                else if (_parsedScripts.TryGetValue(script, out var parsed))
                {
                    // A script parsed by ParseClassicScript (the script of a worker) is not parsed again
                    _parsedScripts.Remove(script);
                    prepared = parsed;
                }
                else if (JsPreparedScript.TryParse(script.SourceText, script.Url, Math.Max(0, script.SourceTextLine - 1), script.SourceTextColumn, out var inlineScript, out var error))
                {
                    // The positions in an inline script are the positions in its document
                    prepared = inlineScript;
                }
                else
                {
                    throw CreateSyntaxError(realm, error);
                }

                return ToObject(engineRealm.Execute(prepared, hostDefined, script.MutedErrors));
            });

            return result;
        }
        finally
        {
            ActiveScripts.Pop();
        }
    }

    /// <summary>The exception that throws the SyntaxError of a script that cannot be parsed, located at the error.</summary>
    internal JsException CreateSyntaxError(WebIdlRealm<JavaScriptAdapter, object> realm, JsSyntaxErrorException error)
    {
        var value = realm.Run(() => realm.CreateError("SyntaxError", error.Message));
        return new JsException(ToJs(value), error.Url is null ? null : new JsSourceLocation(error.Url, error.Line, error.Column));
    }

    /// <inheritdoc/>
    protected internal override object? ParseClassicScript(ClassicScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (JsPreparedScript.TryParse(script.SourceText, script.Url, 0, 0, out var prepared, out var error))
        {
            _parsedScripts.AddOrUpdate(script, prepared);
            return null;
        }

        var realm = Agent.GetRealm(script.Global);
        return realm.Run(() => realm.CreateError("SyntaxError", error.Message));
    }

    /// <summary>The base URLs and the fetch options of the running classic and module scripts (the innermost is the active script).</summary>
    internal Stack<ScriptHostDefined> ActiveScripts { get; } = new();

    /// <summary>
    /// The URLs of the classic scripts whose errors are muted (the last script run for a URL wins): the errors that their
    /// code throws are muted (see <see cref="ExtractErrorInformation(object?)"/>).
    /// </summary>
    internal ConcurrentDictionary<string, bool> MutedScriptLocations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The incumbent global object while a script runs (https://html.spec.whatwg.org/multipage/webappapis.html#incumbent):
    /// the global object of the realm of the code that calls the platform (e.g. the source of the messages of
    /// <c>postMessage()</c>), the code of the bindings runs in the realm of its caller.
    /// </summary>
    protected internal override GlobalScope? GetCurrentGlobal() => IsScriptRunning ? GetIncumbentGlobal() : null;

    /// <summary>The incumbent global objects of the job callbacks being called (the backup incumbent settings object stack).</summary>
    internal Stack<GlobalScope?> BackupIncumbentGlobals { get; } = new();

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#incumbent: the global object of the realm of the topmost
    /// script-having execution context (the active script), else of the backup incumbent settings object stack, else of
    /// the current realm.
    /// </summary>
    internal GlobalScope? GetIncumbentGlobal()
    {
        if (Engine.GetActiveScriptOrModule() is { } script && Agent.GetRealm(script.Realm) is { } realm)
            return realm.GlobalScope;

        if (BackupIncumbentGlobals.TryPeek(out var incumbent) && incumbent is not null)
            return incumbent;

        return Agent.CurrentRealm?.GlobalScope;
    }

    /// <summary>
    /// The [[HostDefined]] of the active script (https://html.spec.whatwg.org/multipage/webappapis.html#active-script): the
    /// script or the module of the innermost running code, else the innermost script run by the engine (e.g. while the code
    /// of the implementation runs). Null when no script is active.
    /// </summary>
    internal ScriptHostDefined? GetActiveScript()
    {
        if (IsScriptRunning && Engine.GetActiveScriptOrModule()?.HostDefined is ScriptHostDefined hostDefined)
            return hostDefined;

        return ActiveScripts.TryPeek(out var script) ? script : null;
    }

    /// <inheritdoc/>
    protected internal override string? GetActiveScriptBaseUrl() => GetActiveScript()?.BaseUrl;

    /// <inheritdoc/>
    protected internal override ModuleFetchOptions? GetActiveScriptFetchOptions() => GetActiveScript()?.FetchOptions;

    /// <inheritdoc/>
    protected internal override ScriptLocation? GetCurrentScriptLocation()
    {
        if (!IsScriptRunning || Engine.GetCurrentLocation() is not { Url: { } url } location)
            return null;

        return new ScriptLocation(url, location.Line, location.Column);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Event handlers
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The [[HostDefined]] of the code of the event handlers: the function has no script (its [[ScriptOrModule]] is null),
    /// so an <c>import()</c> resolves against the base URL of the document.
    /// </summary>
    private static readonly ScriptHostDefined EventHandlerHostDefined = new() { BaseUrl = null, IsEventHandler = true };

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler (step 1.9)</summary>
    protected internal override EventHandlerCallback CompileEventHandler(EventHandlerSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var realm = Agent.GetRealm(source.Window);
        using var scope = EnterJavaScript();
        var scopes = "";
        if (source.Document is not null)
        {
            scopes += "with (__document) ";
        }

        if (source.Form is not null)
        {
            scopes += "with (__form) ";
        }

        if (source.Element is not null)
        {
            scopes += "with (__element) ";
        }

        // The scope of the function is the element, the form and the document (the object environments of the with
        // statements); the parameters (e.g. event) shadow their properties. The function is created by OrdinaryFunctionCreate:
        // its source text has the name of the attribute, but the name is not bound in its scope (e.g. onload() in the
        // handler calls the onload function of the global object), so the function expression is parsed as an anonymous
        // function, named by the key of the property definition
        var prefix = "(function (__element, __form, __document) { " + scopes + "return { " + source.Name + ": ";
        var code = prefix + "function " + source.Name + "(" + string.Join(", ", source.Parameters) + ") {\n" + source.Body + "\n} }." + source.Name + "; })";
        var function = realm.Run(() =>
        {
            if (!JsPreparedScript.TryParse(code, source.Url, 0, 0, unboundNameFunctionStart: prefix.Length, out var prepared, out var error))
                throw CreateSyntaxError(realm, error);

            var engineRealm = (JsRealm)realm.EngineRealm;
            var factory = engineRealm.Execute(prepared, EventHandlerHostDefined);
            return ToObject(Engine.Call(factory, JsValue.Undefined, ToJsOrNull(realm, source.Element), ToJsOrNull(realm, source.Form), ToJsOrNull(realm, source.Document)));
        });

        return realm.CreateEventHandlerCallback(function, isOnBeforeUnloadEventHandler: source.Name == "onbeforeunload");

        static JsValue ToJsOrNull(WebIdlRealm<JavaScriptAdapter, object> realm, object? value) => value is null ? JsValue.Null : ToJs(realm.ToJs(value));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Event loop
    // ----------------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected internal override object? StructuredSerialize(GlobalScope global, object? value) => Agent.GetRealm(global).StructuredSerialize(value);

    /// <inheritdoc/>
    protected internal override object? StructuredSerialize(GlobalScope global, object? value, IReadOnlyList<object> transferList) => Agent.GetRealm(global).StructuredSerialize(value, transferList);

    /// <inheritdoc/>
    protected internal override object? StructuredSerializeForStorage(GlobalScope global, object? value) => Agent.GetRealm(global).StructuredSerialize(value, forStorage: true);

    /// <inheritdoc/>
    protected internal override object? StructuredSerializeForAgent(GlobalScope global, object? value, IReadOnlyList<object> transferList) => Agent.GetRealm(global).StructuredSerializeForAgent(value, transferList);

    /// <inheritdoc/>
    protected internal override object? StructuredDeserialize(GlobalScope global, object? serialized) => Agent.GetRealm(global).StructuredDeserialize(serialized);

    internal override IIndexedDBValueOperations? GetIndexedDBValueOperations(GlobalScope global) => Agent.GetRealm(global);

    internal override IServiceWorkerFetchOperations? GetServiceWorkerFetchOperations(GlobalScope global) => Agent.GetRealm(global);

    /// <inheritdoc/>
    protected internal override void WaitForAll(GlobalScope global, IReadOnlyList<object?> values, Action successSteps, Action<object?> failureSteps) => Agent.GetRealm(global).WaitForAll(values, successSteps, failureSteps);

    /// <inheritdoc/>
    protected internal override IDisposable PrepareToRunScript(GlobalScope global)
    {
        ArgumentNullException.ThrowIfNull(global);
        EnterJavaScript();
        return new PreparedScriptScope(this, global);
    }

    private sealed class PreparedScriptScope(JavaScriptScriptEngine engine, GlobalScope window) : IDisposable
    {
        public void Dispose()
        {
            engine.ExitJavaScript();
            engine.CleanUpAfterRunningScript(window);
        }
    }

    /// <inheritdoc/>
    protected internal override void QueueMicrotask(Action microtask) => Engine.EnqueueJob(microtask);

    /// <inheritdoc/>
    protected internal override void PerformMicrotaskCheckpoint()
    {
        using var scope = EnterJavaScript();
        try
        {
            Engine.RunJobs();
        }
        catch (JsException ex)
        {
            ReportException(Agent.CurrentRealm, ex);
        }
    }

    /// <inheritdoc/>
    protected internal override IDisposable EnterNestedMicrotaskQueue()
    {
        // The jobs queued in the scope go to a new queue
        var queue = Engine.EnterNestedJobQueue();
        _executionDepth++;
        return new NestedMicrotaskQueueScope(this, queue);
    }

    private sealed class NestedMicrotaskQueueScope(JavaScriptScriptEngine engine, IDisposable queue) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            queue.Dispose();
            engine._executionDepth--;
        }
    }

    /// <summary>Whether JavaScript code is running: the .NET code that calls into the engine uses <see cref="EnterJavaScript"/>.</summary>
    protected internal override bool IsScriptRunning => _executionDepth > 0;

    /// <summary>Marks the beginning of a call from .NET into JavaScript code (the JavaScript execution context stack is not empty until the scope is disposed).</summary>
    internal ExecutionScope EnterJavaScript()
    {
        _executionDepth++;
        return new ExecutionScope(this);
    }

    internal void ExitJavaScript() => _executionDepth--;

    internal readonly struct ExecutionScope(JavaScriptScriptEngine engine) : IDisposable
    {
        public void Dispose() => engine.ExitJavaScript();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#clean-up-after-running-script</summary>
    internal void CleanUpAfterRunningScript(GlobalScope window)
    {
        if (!IsScriptRunning)
        {
            window.EventLoop.PerformMicrotaskCheckpoint();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Errors
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Reports an exception to the global object of a realm (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception).</summary>
    internal void ReportException(WebIdlRealm<JavaScriptAdapter, object>? realm, Exception exception)
    {
        realm ??= Agent.CurrentRealm;
        realm?.GlobalScope.ReportException(ExtractErrorInformation(exception));
    }

    /// <inheritdoc/>
    protected internal override ScriptError ExtractErrorInformation(object? exception) => ExtractErrorInformation(exception, useStackLocation: true);

    /// <summary>Extracts the information of an exception.</summary>
    /// <param name="exception">The exception or the value.</param>
    /// <param name="useStackLocation">Whether an error object that was not thrown is located by the first frame of its stack (the caller may know better: e.g. the parse error of a module).</param>
    internal ScriptError ExtractErrorInformation(object? exception, bool useStackLocation)
    {
        var realm = Agent.CurrentRealm;
        using var engineScope = Engine.Enter();
        var value = exception switch
        {
            JsException jsException => jsException.Value,
            Exception ex when realm is not null => ToJs(Agent.GetExceptionValue(ex, realm)),
            JsObject or JsSymbol or UndefinedValue or NullValue => ToJs(exception),
            null => JsValue.Null,
            _ when realm is not null => ToJs(realm.ToJs(exception)),
            _ => JsValue.FromString(exception.ToString() ?? ""),
        };

        var error = ToObject(value);

        // The location of the error (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception): where the
        // exception was thrown, in the script whose URL is the source of the code
        if (exception is JsException { Location: { Url: { } url } location })
        {
            // https://html.spec.whatwg.org/multipage/webappapis.html#muted-errors: the errors thrown by the code of a classic
            // script whose errors are muted (a cross-origin script fetched without CORS) are muted, also when the code is
            // called later by another script or by a callback (the specification mutes the errors of the script that runs;
            // the browsers mute the errors thrown by the code of a muted script)
            if (MutedScriptLocations.ContainsKey(url))
                return ScriptError.Muted();

            return new ScriptError(GetErrorMessage(value), error)
            {
                Filename = url,
                LineNumber = location.Line,
                ColumnNumber = location.Column,
            };
        }

        // An error object that was not thrown (e.g. the reason of a rejected promise, or the argument of reportError()) is
        // located where it was created, as in the browsers: the first frame of its stack
        if (useStackLocation && exception is not JsException && value.TryGetObject() is { } errorObject && errorObject.HasInternalSlot(Meziantou.Framework.JavaScriptEngine.JsInternalSlot.ErrorData) && TryGetStackLocation(errorObject, out var stackUrl, out var stackLine, out var stackColumn))
        {
            return new ScriptError(GetErrorMessage(value), error)
            {
                Filename = stackUrl,
                LineNumber = stackLine,
                ColumnNumber = stackColumn,
            };
        }

        return new ScriptError(GetErrorMessage(value), error);
    }

    /// <summary>Parses the first frame of the stack of an error object ("at name (url:line:column)" or "at url:line:column").</summary>
    private static bool TryGetStackLocation(JsObject error, [NotNullWhen(true)] out string? url, out int line, out int column)
    {
        url = null;
        line = 0;
        column = 0;
        if (error.GetErrorStack() is not { } stack)
            return false;

        foreach (var range in stack.AsSpan().Split('\n'))
        {
            var frame = stack.AsSpan(range).Trim();
            if (!frame.StartsWith("at ", StringComparison.Ordinal))
                continue;

            frame = frame[3..];
            if (frame.EndsWith(")", StringComparison.Ordinal) && frame.LastIndexOf(" (", StringComparison.Ordinal) is >= 0 and var start)
            {
                frame = frame[(start + 2)..^1];
            }

            var columnSeparator = frame.LastIndexOf(':');
            if (columnSeparator <= 0 || !int.TryParse(frame[(columnSeparator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out column))
                return false;

            frame = frame[..columnSeparator];
            var lineSeparator = frame.LastIndexOf(':');
            if (lineSeparator <= 0 || !int.TryParse(frame[(lineSeparator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out line))
                return false;

            // The code that has no URL is not located
            url = frame[..lineSeparator].ToString();
            return url != "<anonymous>";
        }

        return false;
    }

    private string GetErrorMessage(JsValue error)
    {
        try
        {
            if (error.TryGetObject() is { } obj && obj.ContainsProperty("message"))
            {
                // The message includes the name of the error, as in the browsers (e.g. "TypeError: message")
                var message = Engine.ToString(obj.GetProperty("message"));
                if (obj.GetProperty("name") is { IsString: true } name && name.AsString() is { Length: > 0 } nameString)
                    return message.Length == 0 ? nameString : nameString + ": " + message;

                return message;
            }

            return Engine.ToString(error);
        }
        catch (JsException)
        {
            return "";
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#the-hostpromiserejectiontracker-implementation</summary>
    internal void OnPromiseRejectionTracker(JsObject promise, JsPromiseRejectionOperation operation)
    {
        if (Agent.CurrentRealm is not { } realm)
            return;

        var reason = promise.TryGetPromiseState(out _, out var result, out _) ? ToObject(result) : UndefinedValue.Instance;
        realm.GlobalScope.TrackPromiseRejection(promise, reason, operation == JsPromiseRejectionOperation.Reject ? Scripting.PromiseRejectionOperation.Reject : Scripting.PromiseRejectionOperation.Handle);
    }

    /// <inheritdoc/>
    protected internal override bool IsPromiseHandled(object promise) => promise is JsObject obj && obj.TryGetPromiseState(out _, out _, out var isHandled) && isHandled;
}
