using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Jint;

/// <summary>
/// A <see cref="ScriptEngine"/> implemented with Jint (https://github.com/sebastienros/jint): set it as the
/// <see cref="BrowsingContextOptions.ScriptEngine"/> of a browsing context to run the scripts of its documents. The windows
/// of the browsing contexts share a Jint engine (the similar-origin window agent), and each window is the global object of
/// its own realm, created on first use. The DOM is exposed with its WebIDL bindings.
/// </summary>
/// <remarks>
/// The engine is not thread-safe: use it from the thread of the <see cref="DomEventLoop"/> of the browsing contexts. See
/// <see cref="JintScriptEngineOptions"/> for the limits of the resources the scripts can use.
/// </remarks>
#pragma warning disable CA1001 // The engine is disposed by Dispose
public partial class JintScriptEngine : ScriptEngine, IDisposable
#pragma warning restore CA1001
{
    private static readonly ConcurrentDictionary<string, Prepared<Acornima.Ast.Script>> PreparedScripts = new(StringComparer.Ordinal);

    private static readonly ScriptPreparationOptions RetainSourceTextPreparationOptions = ScriptPreparationOptions.Default with { ParsingOptions = ScriptParsingOptions.Default with { RetainFunctionSourceText = true } };

    private readonly JsValue _queueJob;
    private int _executionDepth;
    private readonly ConditionalWeakTable<JsValue, object> _handledPromises = [];
    private readonly ConditionalWeakTable<JsValue, JsValue> _rejectionReasons = [];

    /// <summary>Creates a script engine.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public JintScriptEngine(JintScriptEngineOptions? options = null)
        : this(options, CancellationToken.None)
    {
    }

    /// <summary>Creates the script engine of a worker (see <see cref="CreateWorkerScriptEngine"/>).</summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <param name="terminationToken">Canceled when the worker is terminated: the running script is aborted.</param>
    protected JintScriptEngine(JintScriptEngineOptions? options, CancellationToken terminationToken)
    {
        options ??= new JintScriptEngineOptions();
        Options = options;
        JsHost? host = null;
        var retainFunctionSourceText = false;
        var moduleLoader = ModuleLoader;
        moduleLoader.Owner = this;
        Engine = new Engine(engineOptions =>
        {
            engineOptions.Strict = false;
            engineOptions.Modules.ModuleLoader = moduleLoader;
            if (options.Timeout is { } timeout)
            {
                engineOptions.TimeoutInterval(timeout);
            }

            if (options.MemoryLimit is { } memoryLimit)
            {
                engineOptions.LimitMemory(memoryLimit);
            }

            if (options.MaxStatements is { } maxStatements)
            {
                engineOptions.MaxStatements(maxStatements);
            }

            if (options.RecursionLimit is { } recursionLimit)
            {
                engineOptions.LimitRecursion(recursionLimit);
            }

            if (options.RegexTimeout is { } regexTimeout)
            {
                engineOptions.RegexTimeoutInterval(regexTimeout);
            }

            if (terminationToken.CanBeCanceled)
            {
                engineOptions.CancellationToken(terminationToken);
            }

            // Function.prototype.toString() returns the source text of the functions (https://tc39.es/ecma262/#sec-function.prototype.tostring)
            engineOptions.RetainFunctionSourceText = true;
            options.ConfigureEngine?.Invoke(engineOptions);
            retainFunctionSourceText = engineOptions.RetainFunctionSourceText;
            engineOptions.UseHostFactory(_ => host = new JsHost(this));
        });

        RetainFunctionSourceText = retainFunctionSourceText;
        Host = host!;
        var state = new JintAdapterState(Engine, host!, retainFunctionSourceText);
        Agent = new WebIdlAgent<JintAdapter, JsValue>(new JintAdapter(state), new WebIdlHost(this))
        {
            RealmFactory = state.CreateRealm,
            RealmCreated = realm =>
            {
                realm.EvaluateBuiltIn(IntrinsicsFixesScript);
                FixProxyOwnPropertyKeys(realm);
                InitializeRealm(realm);
                InstallDynamicCodeHooks(realm);
                OnRealmCreated(realm.GlobalScope, realm.Global.AsObject());
            },
        };
        _queueJob = Engine.Evaluate("(function (job) { Promise.resolve().then(job); })");
        Engine.Advanced.PromiseRejectionTracker += OnPromiseRejectionTracker;
    }

    /// <summary>
    /// Replaces the intrinsics of Jint that do not follow the specification, in each realm:
    /// <list type="bullet">
    /// <item>Error.prototype.toString (https://tc39.es/ecma262/#sec-error.prototype.tostring): Jint gets the name and the
    /// message with Error.prototype as the receiver, so the getters of the platform objects (e.g. DOMException) throw.</item>
    /// </list>
    /// </summary>
    private const string IntrinsicsFixesScript = """
        (function () {
          'use strict';
          const toString = {
            toString() {
              if ((typeof this !== 'object' && typeof this !== 'function') || this === null) throw new TypeError('Error.prototype.toString called on a non-object');
              const name = this.name;
              const nameString = name === undefined ? 'Error' : String(name);
              const message = this.message;
              const messageString = message === undefined ? '' : String(message);
              if (nameString === '') return messageString;
              if (messageString === '') return nameString;
              return nameString + ': ' + messageString;
            },
          }.toString;
          Object.defineProperty(Error.prototype, 'toString', { value: toString, writable: true, enumerable: false, configurable: true });
        })();
        """;

    /// <summary>
    /// Replaces <c>Object.getOwnPropertyNames</c>, <c>Object.getOwnPropertySymbols</c>, <c>Object.keys</c>,
    /// <c>Object.values</c> and <c>Object.entries</c> for the proxies: Jint returns all the keys of the <c>ownKeys</c> trap,
    /// without keeping only the strings or the symbols (https://tc39.es/ecma262/#sec-getownpropertykeys,
    /// https://tc39.es/ecma262/#sec-enumerableownproperties). The other objects are given to the functions of Jint.
    /// </summary>
    private static void FixProxyOwnPropertyKeys(WebIdlRealm<JintAdapter, JsValue> realm)
    {
        // The type of the proxies of Jint is internal
        var proxyType = realm.Evaluate("new Proxy({}, {})").GetType();
        var objectConstructor = realm.Global.Get("Object").AsObject();
        Replace("getOwnPropertyNames", proxy => [.. proxy.GetOwnPropertyKeys().Where(key => key.IsString())]);
        Replace("getOwnPropertySymbols", proxy => [.. proxy.GetOwnPropertyKeys().Where(key => key.IsSymbol())]);
        Replace("keys", proxy => EnumerableOwnProperties(proxy, key => key));
        Replace("values", proxy => EnumerableOwnProperties(proxy, key => proxy.Get(key)));
        Replace("entries", proxy => EnumerableOwnProperties(proxy, key => realm.Adapter.CreateArray([key, proxy.Get(key)])));

        void Replace(string name, Func<ObjectInstance, JsValue[]> getProxyResult)
        {
            var original = objectConstructor.Get(name);
            var function = realm.Adapter.CreateFunction(name, 1, (thisValue, arguments) =>
            {
                if (arguments.Length == 0 || arguments[0] is not ObjectInstance proxy || proxy.GetType() != proxyType)
                    return realm.Adapter.Call(original, thisValue, arguments);

                // The array is created in the realm of the function
                return realm.Adapter.RunInRealm(realm.EngineRealm, () => realm.Adapter.CreateArray(getProxyResult(proxy)));
            });
            objectConstructor.DefineOwnProperty(name, new global::Jint.Runtime.Descriptors.PropertyDescriptor(function, writable: true, enumerable: false, configurable: true));
        }

        static JsValue[] EnumerableOwnProperties(ObjectInstance proxy, Func<JsValue, JsValue> select)
        {
            var result = new List<JsValue>();
            foreach (var key in proxy.GetOwnPropertyKeys())
            {
                if (!key.IsString())
                    continue;

                var descriptor = proxy.GetOwnProperty(key);
                if (descriptor != global::Jint.Runtime.Descriptors.PropertyDescriptor.Undefined && descriptor.Enumerable)
                {
                    result.Add(select(key));
                }
            }

            return [.. result];
        }
    }

    /// <summary>Gets the Jint engine.</summary>
    public Engine Engine { get; }

    /// <summary>Gets the options of the engine, also used by the engines of the workers.</summary>
    protected JintScriptEngineOptions Options { get; }

    /// <summary>Creates the engine of a worker: a new Jint engine with the same options (a subclass should override it to create an instance of its own type).</summary>
    protected internal override ScriptEngine? CreateWorkerScriptEngine(CancellationToken terminationToken) => new JintScriptEngine(Options, terminationToken);

    /// <summary>Whether the parsed functions retain their source text (<see cref="Options.RetainFunctionSourceText"/>), for the prepared scripts and the modules.</summary>
    internal bool RetainFunctionSourceText { get; }

    internal WebIdlAgent<JintAdapter, JsValue> Agent { get; }

    /// <summary>
    /// Called when the realm of a window is created, before a script runs in it: the host can add its own functions to the
    /// global object.
    /// </summary>
    /// <param name="global">The .NET global object: a <see cref="Window"/>, or the global object of a worker.</param>
    /// <param name="globalObject">The global object of the realm, whose prototype chain implements the interface of <paramref name="global"/>.</param>
    protected virtual void OnRealmCreated(GlobalScope global, ObjectInstance globalObject)
    {
    }

    /// <summary>Initializes the global object of a new realm (used by the web-platform-tests runner).</summary>
    internal virtual void InitializeRealm(WebIdlRealm<JintAdapter, JsValue> realm)
    {
    }

    /// <summary>
    /// Evaluates code in the realm of a window, as an indirect <c>eval</c>. The microtasks are not run: call
    /// <see cref="DomEventLoop.PerformMicrotaskCheckpoint"/> afterwards.
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
        return realm.Run(() => realm.Evaluate(code));
    }

    /// <summary>Releases the Jint engine.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the Jint engine.</summary>
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

    /// <inheritdoc/>
    protected internal override void OnDocumentActivated(Document document)
    {
        // The window of an initial about:blank document is reused by the new document
        if (document.RelevantWindow is { } window && Agent.TryGetRealm(window) is { } realm)
        {
            realm.SyncFrames();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnChildNavigablesChanged(Window window)
    {
        if (Agent.TryGetRealm(window) is { } realm)
        {
            realm.SyncFrames();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scripts
    // ----------------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected internal override object? ExecuteClassicScript(ClassicScript script)
    {
        var realm = Agent.GetRealm(script.Global);
        InstallDynamicCodeHooks(realm);
        using var scope = EnterJavaScript();
        ActiveScripts.Push((script.BaseUrl, script.FetchOptions));
        ClassicScriptBaseUrls[script.Url] = script.BaseUrl;
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
                if (ShouldCacheScript(script))
                {
                    // The prepared scripts are shared by the engines: the ones that retain the source text of the functions are distinct
                    var retainSourceText = RetainFunctionSourceText;
                    var prepared = PreparedScripts.GetOrAdd((retainSourceText ? "source|" : "") + script.Url, (_, source) => Engine.PrepareScript(source.SourceText, source.Url, options: retainSourceText ? RetainSourceTextPreparationOptions : null), script);
                    return Engine.Evaluate(prepared);
                }

                // A script parsed by ParseClassicScript (the script of a worker) is not parsed again
                if (_parsedScripts.TryGetValue(script, out var parsed))
                {
                    _parsedScripts.Remove(script);
                    return Engine.Evaluate(parsed.Value);
                }

                // The positions in an inline script are the positions in its document (the source text is padded)
                var sourceText = script.SourceText;
                if (script.SourceTextLine > 1 || script.SourceTextColumn > 0)
                {
                    sourceText = new string('\n', Math.Max(0, script.SourceTextLine - 1)) + new string(' ', script.SourceTextColumn) + sourceText;
                }

                return Engine.Evaluate(sourceText, script.Url);
            });

            // A JavaScript string is returned as a .NET string (JsValue has an implicit conversion from string)
            if (result.IsString())
                return result.AsString();

            return result;
        }
        catch (Exception ex) when (ex is Acornima.ParseErrorException or ScriptPreparationException)
        {
            throw new JavaScriptException(realm.CreateError("SyntaxError", ex.Message));
        }
        finally
        {
            ActiveScripts.Pop();
        }
    }

    private readonly ConditionalWeakTable<ClassicScript, StrongBox<Prepared<Acornima.Ast.Script>>> _parsedScripts = [];

    /// <inheritdoc/>
    protected internal override object? ParseClassicScript(ClassicScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        try
        {
            var prepared = Engine.PrepareScript(script.SourceText, script.Url, options: RetainFunctionSourceText ? RetainSourceTextPreparationOptions : null);
            _parsedScripts.AddOrUpdate(script, new StrongBox<Prepared<Acornima.Ast.Script>>(prepared));
            return null;
        }
        catch (Exception ex) when (ex is Acornima.ParseErrorException or ScriptPreparationException)
        {
            var realm = Agent.GetRealm(script.Global);
            return realm.Run(() => realm.CreateError("SyntaxError", ex.Message));
        }
    }

    /// <summary>The base URLs and the fetch options of the running classic and module scripts (the innermost is the active script).</summary>
    internal Stack<(string BaseUrl, ModuleFetchOptions FetchOptions)> ActiveScripts { get; } = new();

    /// <summary>
    /// The base URLs of the classic scripts given their URL (the source name of their code): the base URL of the document when
    /// an inline script was prepared (the last one for the inline scripts of a document), for an <c>import()</c> that runs
    /// once the script is no longer active (e.g. in code compiled by <c>eval()</c> in a promise job).
    /// </summary>
    internal Dictionary<string, string> ClassicScriptBaseUrls { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The URLs of the classic scripts whose errors are muted (the last script run for a URL wins): their base URL is
    /// about:blank, also for an <c>import()</c> that runs once the script is no longer active (Jint gives the URL of the
    /// referencing script), and the errors that their code throws are muted (see <see cref="ExtractErrorInformation(object?)"/>).
    /// </summary>
    internal ConcurrentDictionary<string, bool> MutedScriptLocations { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    protected internal override GlobalScope? GetCurrentGlobal() => IsScriptRunning ? Agent.CurrentRealm?.GlobalScope : null;

    /// <inheritdoc/>
    protected internal override string? GetActiveScriptBaseUrl()
    {
        if (ActiveScripts.TryPeek(out var script))
            return script.BaseUrl;

        // No script runs (e.g. a promise job, whose active script is not tracked): the active script is the script or module of
        // the innermost running function (https://tc39.es/ecma262/#sec-getactivescriptormodule), e.g. the code that calls setTimeout()
        if (IsScriptRunning && GetActiveScriptOrModuleLocation() is { } location)
        {
            if (MutedScriptLocations.ContainsKey(location))
                return "about:blank";

            if (ModuleLoader.IsModule(location))
                return ModuleLoader.GetBaseUrl(location);

            if (ClassicScriptBaseUrls.TryGetValue(location, out var baseUrl))
                return baseUrl;
        }

        return null;
    }

    /// <inheritdoc/>
    protected internal override ScriptLocation? GetCurrentScriptLocation()
    {
        // The last node evaluated by Jint is the current expression (e.g. the call of the operation that reports the violation),
        // or the callee of the call of a hook of the dynamic code (the last evaluated node may be an argument of eval())
        var node = IsScriptRunning ? GetDynamicCodeCallee() ?? GetLastSyntaxElement(Engine) : null;

        // The location of a call of a method (the last evaluated node is the call or its callee) is the one of the name of
        // the method, as in the browsers (e.g. "createPolicy" in "trustedTypes.createPolicy()")
        var callee = node switch
        {
            Acornima.Ast.CallExpression call => call.Callee,
            Acornima.Ast.NewExpression construct => construct.Callee,
            _ => node,
        };

        if (callee is Acornima.Ast.MemberExpression { Computed: false } member)
        {
            node = member.Property;
        }

        if (node is not { Location: { SourceFile: { } sourceFile } location })
            return null;

        var url = EventHandlerSourceNames.TryGetValue(sourceFile, out var documentUrl) ? documentUrl : ModuleLoader.GetSourceUrl(sourceFile);
        return new ScriptLocation(url, location.Start.Line, location.Start.Column + 1);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetLastSyntaxElement")]
    private static safe extern Acornima.Ast.Node? GetLastSyntaxElement(Engine engine);

    /// <summary>The location (source name) of the active script or module of Jint, or null.</summary>
    private string? GetActiveScriptOrModuleLocation() => GetActiveScriptOrModule(Engine) switch
    {
        global::Jint.Runtime.Modules.Module module => module.Location,
        { } scriptRecord => ScriptRecordLocation(scriptRecord),
        null => null,
    };

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetActiveScriptOrModule")]
    [return: UnsafeAccessorType("Jint.Runtime.IScriptOrModule, Jint")]
    private static safe extern object? GetActiveScriptOrModule(Engine engine);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Location")]
    private static safe extern string? ScriptRecordLocation([UnsafeAccessorType("Jint.Runtime.ScriptRecord, Jint")] object scriptRecord);

    /// <inheritdoc/>
    protected internal override ModuleFetchOptions? GetActiveScriptFetchOptions() => ActiveScripts.TryPeek(out var script) ? script.FetchOptions : null;

    // ----------------------------------------------------------------------------------------------------------------
    // Event handlers
    // ----------------------------------------------------------------------------------------------------------------

    private const string EventHandlerSourceNameSuffix = " (event handler)";

    /// <summary>The source names of the code of the event handlers (see <see cref="CompileEventHandler"/>), and the URL of their document.</summary>
    internal ConcurrentDictionary<string, string> EventHandlerSourceNames { get; } = new(StringComparer.Ordinal);

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler (step 1.9)</summary>
    protected internal override EventHandlerCallback CompileEventHandler(EventHandlerSource source)
    {
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

        // The scope of the function is the element, the form and the document (the parameters, e.g. event, shadow their properties)
        var code = "(function (__element, __form, __document) { " + scopes + "return function " + source.Name + "(" + string.Join(", ", source.Parameters) + ") {\n" + source.Body + "\n}; })";
        // https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler: the function
        // has no script (its [[ScriptOrModule]] is null), so an import() resolves against the base URL of the document. The
        // code is evaluated with its own source name, recognized by the module loader, and reported as the URL of the document.
        var documentUrl = source.Window.Document.Url;
        var sourceName = documentUrl + EventHandlerSourceNameSuffix;
        EventHandlerSourceNames.TryAdd(sourceName, documentUrl);
        var function = realm.Run(() =>
        {
            JsValue factory;
            try
            {
                factory = Engine.Evaluate(code, sourceName);
            }
            catch (Exception ex) when (ex is Acornima.ParseErrorException or ScriptPreparationException)
            {
                throw new JavaScriptException(realm.CreateError("SyntaxError", ex.Message));
            }

            return Engine.Call(factory, JsValue.Undefined, [ToJs(realm, source.Element), ToJs(realm, source.Form), ToJs(realm, source.Document)]);
        });

        return realm.CreateEventHandlerCallback(function, isOnBeforeUnloadEventHandler: source.Name == "onbeforeunload");

        static JsValue ToJs(WebIdlRealm<JintAdapter, JsValue> realm, object? value) => value is null ? JsValue.Null : realm.ToJs(value);
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

    private sealed class PreparedScriptScope(JintScriptEngine engine, GlobalScope window) : IDisposable
    {
        public void Dispose()
        {
            engine.ExitJavaScript();
            engine.CleanUpAfterRunningScript(window);
        }
    }

    /// <inheritdoc/>
    protected internal override void QueueMicrotask(Action microtask)
    {
        Engine.Call(_queueJob, JsValue.Undefined, [new ClrFunction(Engine, "", (_, _) =>
        {
            microtask();
            return JsValue.Undefined;
        })]);
    }

    /// <inheritdoc/>
    protected internal override void PerformMicrotaskCheckpoint()
    {
        using var scope = EnterJavaScript();
        try
        {
            Engine.Advanced.ProcessTasks();
        }
        catch (JavaScriptException ex)
        {
            ReportException(Agent.CurrentRealm, ex);
        }
    }

    /// <inheritdoc/>
    protected internal override IDisposable EnterNestedMicrotaskQueue()
    {
        // The job queue of Jint is the event loop of its engine: a new one receives the jobs queued in the scope
        var scope = new NestedMicrotaskQueueScope(this, JintEventLoop.Get(Engine));
        JintEventLoop.Set(Engine, JintEventLoop.Create());
        _executionDepth++;
        return scope;
    }

    private sealed class NestedMicrotaskQueueScope(JintScriptEngine engine, object eventLoop) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            JintEventLoop.Set(engine.Engine, eventLoop);
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

    internal readonly struct ExecutionScope(JintScriptEngine engine) : IDisposable
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
    internal void ReportException(WebIdlRealm<JintAdapter, JsValue>? realm, Exception exception)
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
        JsValue value = exception switch
        {
            JavaScriptException javaScriptException => javaScriptException.Error,
            Exception ex when realm is not null => Agent.GetExceptionValue(ex, realm),
            JsValue jsValue => jsValue,
            null => JsValue.Null,
            _ when realm is not null => realm.ToJs(exception),
            _ => new JsString(exception.ToString() ?? ""),
        };

        // The location of the error (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception): where the
        // exception was thrown, in the script whose source name is its URL
        if (exception is JavaScriptException { Location: var location } && location.SourceFile is { } sourceFile)
        {
            // https://html.spec.whatwg.org/multipage/webappapis.html#muted-errors: the errors thrown by the code of a classic
            // script whose errors are muted (a cross-origin script fetched without CORS) are muted, also when the code is
            // called later by another script or by a callback (the specification mutes the errors of the script that runs;
            // the browsers mute the errors thrown by the code of a muted script)
            if (MutedScriptLocations.ContainsKey(sourceFile))
                return ScriptError.Muted();

            return new ScriptError(GetErrorMessage(value), value)
            {
                Filename = EventHandlerSourceNames.TryGetValue(sourceFile, out var documentUrl) ? documentUrl : ModuleLoader.GetSourceUrl(sourceFile),
                LineNumber = location.Start.Line,
                ColumnNumber = location.Start.Column + 1,
            };
        }

        // An error object that was not thrown (e.g. the reason of a rejected promise, or the argument of reportError()) is
        // located where it was created, as in the browsers: the first frame of its stack
        if (useStackLocation && exception is not JavaScriptException && value is JsError && TryGetStackLocation(value, out var stackSourceFile, out var stackLine, out var stackColumn))
        {
            return new ScriptError(GetErrorMessage(value), value)
            {
                Filename = EventHandlerSourceNames.TryGetValue(stackSourceFile, out var documentUrl) ? documentUrl : ModuleLoader.GetSourceUrl(stackSourceFile),
                LineNumber = stackLine,
                ColumnNumber = stackColumn,
            };
        }

        return new ScriptError(GetErrorMessage(value), value);
    }

    /// <summary>Parses the first frame of the stack of an error object ("at name (source:line:column)" or "at source:line:column").</summary>
    private static bool TryGetStackLocation(JsValue error, [NotNullWhen(true)] out string? sourceFile, out int line, out int column)
    {
        sourceFile = null;
        line = 0;
        column = 0;
        string stack;
        try
        {
            if (error.Get("stack") is not JsString stackValue)
                return false;

            stack = stackValue.ToString();
        }
        catch (JavaScriptException)
        {
            return false;
        }

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

            // The code that has no source name is not located
            sourceFile = frame[..lineSeparator].ToString();
            return sourceFile != "<anonymous>";
        }

        return false;
    }

    private static string GetErrorMessage(JsValue error)
    {
        try
        {
            if (error is ObjectInstance obj && obj.HasProperty("message"))
            {
                // The message includes the name of the error, as in the browsers (e.g. "TypeError: message")
                var message = TypeConverter.ToString(obj.Get("message"));
                if (obj.Get("name") is JsString { Length: > 0 } name)
                    return message.Length == 0 ? name.ToString() : name + ": " + message;

                return message;
            }

            return TypeConverter.ToString(error);
        }
        catch (JavaScriptException)
        {
            return "";
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#the-hostpromiserejectiontracker-implementation</summary>
    private void OnPromiseRejectionTracker(object? sender, PromiseRejectionTrackerEventArgs e)
    {
        if (Agent.CurrentRealm is not { } realm)
            return;

        if (e.Operation == global::Jint.Native.Promise.PromiseRejectionOperation.Reject)
        {
            // The evaluation promise of a module is always handled (by the module script or the import() that evaluates it)
            if (ModuleLoader.IsEvaluationPromise(e.Promise))
                return;

            var reason = e.Value ?? JsValue.Undefined;
            _rejectionReasons.AddOrUpdate(e.Promise, reason);
            _handledPromises.Remove(e.Promise);
            realm.GlobalScope.TrackPromiseRejection(e.Promise, reason, Scripting.PromiseRejectionOperation.Reject);
        }
        else
        {
            _handledPromises.AddOrUpdate(e.Promise, true);
            var reason = _rejectionReasons.TryGetValue(e.Promise, out var value) ? value : JsValue.Undefined;
            realm.GlobalScope.TrackPromiseRejection(e.Promise, reason, Scripting.PromiseRejectionOperation.Handle);
        }
    }

    /// <inheritdoc/>
    protected internal override bool IsPromiseHandled(object promise) => promise is JsValue value && _handledPromises.TryGetValue(value, out _);
}

/// <summary>The services of the script engine used by the WebIDL bindings.</summary>
internal sealed class WebIdlHost(JintScriptEngine engine) : IWebIdlHost
{
    public void ReportException(GlobalScope global, Exception exception) => global.ReportException(engine.ExtractErrorInformation(exception));

    public void OnCallbackInvoking() => engine.EnterJavaScript();

    public bool IsScriptRunning => engine.IsScriptRunning;

    /// <summary>Throws when a limit of the engine is exceeded (e.g. the timeout), while WebAssembly code runs.</summary>
    public void CheckInterrupt() => engine.Engine.Constraints.Check();

    public void OnCallbackInvoked(GlobalScope global)
    {
        engine.ExitJavaScript();
        engine.CleanUpAfterRunningScript(global);
    }
}
