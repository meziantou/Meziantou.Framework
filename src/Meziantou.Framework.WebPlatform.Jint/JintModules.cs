using System.Runtime.CompilerServices;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Jint.Runtime.Modules;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Jint;

// Module scripts (https://html.spec.whatwg.org/multipage/webappapis.html#module-script) with the module records of Jint:
// the module graphs are loaded asynchronously by JintModuleLoader, which fetches the modules with the library.
public partial class JintScriptEngine
{
    private JintModuleLoader ModuleLoader { get; } = new();

    /// <inheritdoc/>
    protected internal override bool SupportsModuleScripts => true;

    /// <inheritdoc/>
    protected internal override void LoadModuleScriptGraph(ModuleScript script, Action<bool> onComplete)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(onComplete);
        var realm = Agent.GetRealm(script.Global);
        using var scope = EnterJavaScript();
        realm.Run(() =>
        {
            Module module;
            try
            {
                module = ModuleLoader.CreateRootModule(Engine, script);
            }
            catch (JavaScriptException ex)
            {
                // A parse error of the root is reported when the script runs. The graph completes from a microtask, as when
                // the root is parsed (in order with the inline scripts that have no error)
                script.ErrorToRethrow = ex.Error;
                QueueMicrotask(() => onComplete(true));
                return JsValue.Undefined;
            }

            LoadDescendants(script, module, onComplete);
            return JsValue.Undefined;
        });
    }

    /// <inheritdoc/>
    protected internal override void FetchModuleScriptGraph(GlobalScope global, string url, ModuleFetchOptions options, Element? element, Action<ModuleScript?> onComplete)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onComplete);

        // The root is the module of the module map of the window (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script):
        // a module already fetched (e.g. by another script element, or as a dependency) is not fetched, parsed nor evaluated
        // again, and its parse error is the same error
        var realm = Agent.GetRealm(global);
        ModuleLoader.LoadRootModule(global, url, options, element, entry =>
        {
            using var scope = EnterJavaScript();
            realm.Run(() =>
            {
                if (entry.FetchFailed)
                {
                    onComplete(null);
                    return JsValue.Undefined;
                }

                var script = new ModuleScript(global, entry.SourceText, entry.Url) { Url = entry.Url, Element = element, FetchOptions = options.WithResponseReferrerPolicy(entry.ReferrerPolicy) };
                if (entry.Module is not { } module)
                {
                    script.ErrorToRethrow = entry.Error;
                    onComplete(script);
                    return JsValue.Undefined;
                }

                LoadDescendants(script, module, success => onComplete(success ? script : null));
                return JsValue.Undefined;
            });
        });
    }

    /// <inheritdoc/>
    protected internal override void PreloadModuleScript(GlobalScope global, string url, ModuleType type, RequestDestination destination, ModuleFetchOptions options, Element? element, Action<bool> onComplete)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onComplete);

        // The module is the one of the module map of the window (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script):
        // a module that is fetched (or being fetched) is not fetched again, and the module scripts and the imports use it
        ModuleLoader.LoadRootModule(global, url, options, element, entry => onComplete(!entry.FetchFailed), type, destination);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#fetch-the-descendants-of-and-link-a-module-script (the loading of the descendants)</summary>
    private void LoadDescendants(ModuleScript script, Module module, Action<bool> onComplete)
    {
        script.Record = module;
        var loading = module.LoadRequestedModules();
        Then(loading, onFulfilled: _ => onComplete(true), onRejected: reason =>
        {
            // A module of the graph that cannot be fetched makes the result null; the other errors (e.g. parse errors) are rethrown
            if (ModuleLoader.IsFetchFailure(reason))
            {
                onComplete(false);
                return;
            }

            script.ErrorToRethrow = reason;
            onComplete(true);
        });
    }

    /// <inheritdoc/>
    protected internal override void ExecuteModuleScript(ModuleScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var realm = Agent.GetRealm(script.Global);
        InstallDynamicCodeHooks(realm);
        using var scope = EnterJavaScript();
        realm.Run(() =>
        {
            if (script.ErrorToRethrow is JsValue error)
            {
                script.EvaluationState = ModuleEvaluationState.Rejected;
                ReportModuleError(script, error);
                return JsValue.Undefined;
            }

            if (script.Record is not Module module)
                return JsValue.Undefined;

            JsValue evaluation;
            ActiveScripts.Push((script.BaseUrl, script.FetchOptions));
            try
            {
                module.Link();
                evaluation = module.Evaluate();
            }
            catch (JavaScriptException ex)
            {
                script.EvaluationState = ModuleEvaluationState.Rejected;
                ReportModuleError(script, ex.Error);
                return JsValue.Undefined;
            }
            finally
            {
                ActiveScripts.Pop();
            }

            // The evaluation promise is settled when the graph has no asynchronous module (a top-level await)
            script.EvaluationState = module is not CyclicModule cyclicModule ? ModuleEvaluationState.Fulfilled
                : AsyncEvaluation(cyclicModule) ? ModuleEvaluationState.Pending
                : EvaluationError(cyclicModule) is not null ? ModuleEvaluationState.Rejected
                : ModuleEvaluationState.Fulfilled;

            // The errors of the evaluation (including a top-level await) are reported
            Then(evaluation, onFulfilled: null, onRejected: reason => ReportModuleError(script, reason));
            return JsValue.Undefined;
        });
    }

    private void ReportModuleError(ModuleScript script, JsValue error)
    {
        var information = ExtractErrorInformation(error, useStackLocation: false);
        if (information.Filename.Length == 0 && ModuleLoader.GetParseErrorLocation(error) is { } location)
        {
            // A parse error is located in the module that cannot be parsed (the script itself for an inline script)
            information = new ScriptError(information.Message, information.Error) { Filename = location.Filename ?? script.Url, LineNumber = location.LineNumber, ColumnNumber = location.ColumnNumber };
        }
        else if (information.Filename.Length == 0 && script.Record is CyclicModule module && AbnormalCompletionLocation(module) is { SourceFile: { } sourceFile } completionLocation)
        {
            // An evaluation error is located where it was thrown (Jint keeps the location of the abrupt completion of the evaluation)
            information = new ScriptError(information.Message, information.Error) { Filename = ModuleLoader.GetSourceUrl(sourceFile), LineNumber = completionLocation.Start.Line, ColumnNumber = completionLocation.Start.Column + 1 };
        }

        script.Global.ReportException(information.Filename.Length > 0 ? information : new ScriptError(information.Message, information.Error) { Filename = script.Url, LineNumber = information.LineNumber, ColumnNumber = information.ColumnNumber });
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_AbnormalCompletionLocation")]
    private static safe extern ref readonly Acornima.SourceLocation AbnormalCompletionLocation(CyclicModule module);

    // https://tc39.es/ecma262/#sec-cyclic-module-records: [[AsyncEvaluation]] and [[EvaluationError]]
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_asyncEvaluation")]
    private static safe extern ref bool AsyncEvaluation(CyclicModule module);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_evalError")]
    private static safe extern ref Completion? EvaluationError(CyclicModule module);

    /// <summary>Calls the then method of a promise with .NET callbacks.</summary>
    private void Then(JsValue promise, Action<JsValue>? onFulfilled, Action<JsValue>? onRejected)
    {
        JsValue fulfilled = onFulfilled is null ? JsValue.Undefined : new ClrFunction(Engine, "", (_, arguments) =>
        {
            onFulfilled(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
            return JsValue.Undefined;
        });
        JsValue rejected = onRejected is null ? JsValue.Undefined : new ClrFunction(Engine, "", (_, arguments) =>
        {
            onRejected(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
            return JsValue.Undefined;
        });
        Engine.Invoke(promise.Get("then"), promise, [fulfilled, rejected]);
    }

    /// <summary>The global object of the current realm of the engine.</summary>
    internal GlobalScope? CurrentGlobal => Agent.CurrentRealm?.GlobalScope;

    /// <summary>import.meta (https://html.spec.whatwg.org/multipage/webappapis.html#hostgetimportmetaproperties): url and resolve().</summary>
    internal List<KeyValuePair<JsValue, JsValue>> GetImportMetaProperties(Module module)
    {
        var baseUrl = ModuleLoader.GetBaseUrl(module.Location);
        var window = CurrentGlobal;
        var resolve = new ClrFunction(Engine, "resolve", (_, arguments) =>
        {
            var specifier = TypeConverter.ToString(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
            if (window is null)
                throw new JavaScriptException(Engine.Intrinsics.TypeError, "The module has no window");

            try
            {
                return ModuleSpecifierResolution.Resolve(window, specifier, baseUrl);
            }
            catch (TypeErrorException ex)
            {
                throw new JavaScriptException(Agent.GetRealm(window).CreateError("TypeError", ex.Message));
            }
        }, 1);
        return [new(JsString.Create("url"), JsString.Create(baseUrl)), new(JsString.Create("resolve"), resolve)];
    }

    /// <summary>
    /// The module loader: the module map of each window (https://html.spec.whatwg.org/multipage/webappapis.html#module-map),
    /// keyed by the URL and the type of the modules. The modules are fetched with <see cref="ModuleFetching"/> (CORS
    /// requests, MIME type checks), parsed in the realm of the window, and shared by the static and dynamic imports.
    /// </summary>
    internal sealed class JintModuleLoader : IModuleLoader, IAsyncModuleLoader
    {
        private readonly ConditionalWeakTable<GlobalScope, Dictionary<string, ModuleMapEntry>> _moduleMaps = [];
        private const string InlineModulePrefix = "inline-module:";

        private readonly ConditionalWeakTable<JsValue, object> _fetchFailures = [];
        private readonly ConditionalWeakTable<JsValue, ErrorLocation> _parseErrorLocations = [];
        private readonly Dictionary<string, string> _inlineModuleUrls = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _baseUrls = new(StringComparer.Ordinal);

        // The fetch options of the modules (https://html.spec.whatwg.org/multipage/webappapis.html#descendant-script-fetch-options):
        // the ones of the root of their graph, or of the classic script that imports them
        private readonly Dictionary<string, ModuleFetchOptions> _fetchOptions = new(StringComparer.Ordinal);

        // The referrers of the fetches of the modules: the base URL of the script that imports them, or null for the document
        // (https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule)
        private readonly Dictionary<string, string?> _referrers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _modules = new(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<GlobalScope, StrongBox<int>> _globalIds = [];

        // The global object that resolved the specifier of each key (the keys are specific to a global object, see GetModuleKey)
        private readonly Dictionary<string, WeakReference<GlobalScope>> _keyGlobals = new(StringComparer.Ordinal);
        private int _nextGlobalId;
        private int _nextInlineModule;

        /// <summary>
        /// The key of the module of a URL in the module map of a global object. Jint caches the modules it loads by key for
        /// the whole engine, while each global object has its own module map (a module is evaluated once per window): the
        /// key is the URL for the first global object that loads modules, and the URL followed by the identifier of the
        /// global object for the other ones (a serialized URL has no space, see <see cref="GetBaseUrl"/>).
        /// </summary>
        private string GetModuleKey(GlobalScope global, string url)
        {
            var id = _globalIds.GetValue(global, _ => new StrongBox<int>(_nextGlobalId++)).Value;
            return id == 0 ? url : url + " (realm " + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        public JintScriptEngine Owner { get; set; } = null!;

        /// <summary>The location of a parse error of a module (see <see cref="ErrorLocation"/>), or null.</summary>
        public ErrorLocation? GetParseErrorLocation(JsValue error) => _parseErrorLocations.TryGetValue(error, out var location) ? location : null;

        /// <summary>Whether a rejection reason is the error of a module that could not be fetched.</summary>
        public bool IsFetchFailure(JsValue reason) => _fetchFailures.TryGetValue(reason, out _);

        /// <summary>
        /// Whether a promise is the evaluation promise of a module of the module maps (the promise returned by Evaluate()):
        /// the module scripts and <c>import()</c> handle it (https://tc39.es/ecma262/#sec-ContinueDynamicImport), but Jint
        /// does not call then() on an evaluation promise that is already rejected when it continues an <c>import()</c>.
        /// </summary>
        public bool IsEvaluationPromise(JsValue promise)
        {
            foreach (var (_, map) in _moduleMaps)
            {
                foreach (var entry in map.Values)
                {
                    if (entry.Module is CyclicModule module && TopLevelCapabilityField?.GetValue(module) is { } capability && ReferenceEquals(CapabilityPromise(capability), promise))
                        return true;
                }
            }

            return false;
        }

        // The type of the field is internal: it cannot be read with UnsafeAccessor
        private static readonly System.Reflection.FieldInfo? TopLevelCapabilityField = typeof(CyclicModule).GetField("_topLevelCapability", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "PromiseInstance")]
        private static safe extern ref JsValue CapabilityPromise([UnsafeAccessorType("Jint.Native.Promise.PromiseCapability, Jint")] object capability);

        /// <summary>The URL of a module given its location (the source name of its code), reported as the file name of its errors: the URL of the document for an inline script.</summary>
        public string GetSourceUrl(string location) => _inlineModuleUrls.TryGetValue(location, out var url) ? url : _modules.Contains(location) ? GetBaseUrl(location) : location;

        /// <summary>Whether a location (the source name of a script or module) is the location of a module.</summary>
        public bool IsModule(string location) => _modules.Contains(location);

        /// <summary>The base URL of a module given its location (the key of the module, or the URL of a classic script).</summary>
        public string GetBaseUrl(string? location)
        {
            if (location is null)
                return "";

            return _baseUrls.TryGetValue(location, out var baseUrl) ? baseUrl : location;
        }

        /// <summary>Parses the root of a module graph; it is added to the module map of the window when it is an external script.</summary>
        public Module CreateRootModule(Engine engine, ModuleScript script)
        {
            var key = script.IsInline ? InlineModulePrefix + (++_nextInlineModule).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + script.BaseUrl : GetModuleKey(script.Global, script.BaseUrl);
            _baseUrls[key] = script.BaseUrl;
            _fetchOptions[key] = script.FetchOptions;
            _modules.Add(key);
            var resolved = new ResolvedSpecifier(new ModuleRequest(key, []), key, Uri: null, SpecifierType.RelativeOrAbsolute);
            var sourceText = script.SourceText;
            if (script.IsInline)
            {
                // The positions in an inline script are the positions in its document (the source text is padded, as the
                // SourceOffset of the classic scripts of Jint)
                _inlineModuleUrls[key] = script.Url;
                if (script.SourceTextLine > 1 || script.SourceTextColumn > 0)
                {
                    sourceText = new string('\n', Math.Max(0, script.SourceTextLine - 1)) + new string(' ', script.SourceTextColumn) + sourceText;
                }
            }

            var module = Build(engine, resolved, sourceText, ModuleType.JavaScript, script.Global);
            if (!script.IsInline)
            {
                var map = _moduleMaps.GetOrCreateValue(script.Global);
                if (!map.ContainsKey(key))
                {
                    map[key] = new ModuleMapEntry { Module = module };
                }
            }

            return module;
        }

        public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
        {
            var engine = Owner.Engine;
            var window = Owner.CurrentGlobal ?? throw new JavaScriptException(engine.Intrinsics.TypeError, "Module scripts need a window");
            // https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule: import() is not allowed in the
            // worklets (the modules of a worklet are the ones added by addModule() and their static imports)
            if (window is WorkletGlobalScope && !IsStaticImport(window, referencingModuleLocation, moduleRequest))
                throw new JavaScriptException(Owner.Agent.GetRealm(window).CreateError("TypeError", "import() is not allowed in a worklet"));

            // ... nor in the service workers: their scripts are the ones stored when they are installed (the static imports
            // of a module service worker are resolved while its module graph is fetched)
            if (window is ServiceWorkerGlobalScope { IsFetchingModuleScriptGraph: false } && !IsStaticImport(window, referencingModuleLocation, moduleRequest))
                throw new JavaScriptException(Owner.Agent.GetRealm(window).CreateError("TypeError", "import() is not allowed in a service worker"));

            var type = GetModuleType(engine, moduleRequest);

            // The base URL of the referencing module, else of the document for an event handler (it has no script), else of the
            // active script, else of the referencing classic script (its URL, or the base URL of the document when an inline
            // script was prepared)
            string baseUrl;
            string? referrer;
            if (referencingModuleLocation is not null && _modules.Contains(referencingModuleLocation))
            {
                baseUrl = GetBaseUrl(referencingModuleLocation);
                referrer = baseUrl;
            }
            else if (referencingModuleLocation is not null && Owner.EventHandlerSourceNames.ContainsKey(referencingModuleLocation))
            {
                baseUrl = window.ApiBaseUrl.Serialize();
                referrer = null;
            }
            else if (Owner.GetActiveScriptBaseUrl() is { } activeScriptBaseUrl)
            {
                baseUrl = activeScriptBaseUrl;
                referrer = baseUrl;
            }
            else if (referencingModuleLocation is not null && Owner.MutedScriptLocations.ContainsKey(referencingModuleLocation))
            {
                baseUrl = "about:blank";
                referrer = baseUrl;
            }
            else if (referencingModuleLocation is not null && Owner.ClassicScriptBaseUrls.TryGetValue(referencingModuleLocation, out var classicScriptBaseUrl))
            {
                baseUrl = classicScriptBaseUrl;
                referrer = referencingModuleLocation == window.ScriptUrl ? null : baseUrl;
            }
            else if (referencingModuleLocation is null || referencingModuleLocation == window.ScriptUrl)
            {
                baseUrl = window.ApiBaseUrl.Serialize();
                referrer = null;
            }
            else
            {
                baseUrl = GetBaseUrl(referencingModuleLocation);
                referrer = baseUrl;
            }

            string url;
            try
            {
                url = ModuleSpecifierResolution.Resolve(window, moduleRequest.Specifier, baseUrl);
            }
            catch (TypeErrorException ex)
            {
                throw new JavaScriptException(Owner.Agent.GetRealm(window).CreateError("TypeError", ex.Message));
            }

            var key = GetModuleKey(window, url);
            if (type != ModuleType.JavaScript)
            {
                key += " (" + type.ToString().ToLowerInvariant() + ")";
            }

            _baseUrls[key] = url;
            _modules.Add(key);
            _keyGlobals[key] = new WeakReference<GlobalScope>(window);
            // The options of the referencing script are unknown when no script is active: Jint does not track the active script
            // in the promise jobs (HostMakeJobCallback), e.g. for an import() after an await
            var fetchOptions = referencingModuleLocation is not null && _fetchOptions.TryGetValue(referencingModuleLocation, out var referencingOptions) ? referencingOptions : Owner.GetActiveScriptFetchOptions();
            _fetchOptions.TryAdd(key, fetchOptions ?? new ModuleFetchOptions { IsUnknownReferrer = true });
            _referrers.TryAdd(key, referrer);

            return new ResolvedSpecifier(moduleRequest, key, Uri: null, SpecifierType.RelativeOrAbsolute);
        }

        /// <summary>
        /// Whether a module request is a static import of the referencing module (one of its requested modules), and not an
        /// <c>import()</c>: Jint resolves both with <see cref="Resolve"/>.
        /// </summary>
        private bool IsStaticImport(GlobalScope global, string? referencingModuleLocation, ModuleRequest moduleRequest)
        {
            if (referencingModuleLocation is null || !_moduleMaps.TryGetValue(global, out var map) || !map.TryGetValue(referencingModuleLocation, out var entry) || entry.Module is not CyclicModule module)
                return false;

            foreach (var request in RequestedModules(module))
            {
                if (request.Specifier == moduleRequest.Specifier)
                    return true;
            }

            return false;
        }

        public Module LoadModule(Engine engine, ResolvedSpecifier resolved) => throw new JavaScriptException(engine.Intrinsics.TypeError, "The modules are loaded asynchronously");

        public void LoadModuleAsync(Engine engine, ResolvedSpecifier resolved, ModuleLoadCompletion completion)
        {
            // The global object that resolved the specifier: the module is loaded by a job, which may run in another realm
            // (e.g. the job of an import() of a classic script run by a task of another window)
            var window = _keyGlobals.TryGetValue(resolved.Key, out var resolvingGlobal) && resolvingGlobal.TryGetTarget(out var global) ? global : Owner.CurrentGlobal;
            if (window is null)
            {
                completion.SetError(new JavaScriptException(engine.Intrinsics.TypeError, "Module scripts need a window"));
                return;
            }

            var type = GetModuleType(engine, resolved.ModuleRequest);
            Load(window, resolved.Key, type, initiator: null, entry =>
            {
                if (entry.Module is { } module)
                {
                    completion.SetModule(module);
                }
                else if (entry.FetchFailed)
                {
                    // https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule: a new TypeError for each import
                    completion.SetError(new JavaScriptException(CreateFetchError(window, GetBaseUrl(resolved.Key))));
                }
                else
                {
                    completion.SetError(new JavaScriptException(entry.Error!));
                }
            });
        }

        /// <summary>
        /// Fetches the root of the graph of an external module script, or gets it from the module map of the window.
        /// <paramref name="onLoaded"/> is called once the entry of the module map is fetched (synchronously when it is).
        /// </summary>
        public void LoadRootModule(GlobalScope window, string url, ModuleFetchOptions options, Element? initiator, Action<ModuleMapEntry> onLoaded, ModuleType type = ModuleType.JavaScript, RequestDestination? destination = null)
        {
            // The key of the module map is the URL and the type of the module, as for the imports (see Resolve)
            var key = GetModuleKey(window, url);
            if (type != ModuleType.JavaScript)
            {
                key += " (" + type.ToString().ToLowerInvariant() + ")";
                _baseUrls[key] = url;
                _keyGlobals[key] = new WeakReference<GlobalScope>(window);
            }

            _baseUrls.TryAdd(key, url);
            _fetchOptions.TryAdd(key, options);
            _modules.Add(key);
            Load(window, key, type, initiator, onLoaded, destination);
        }

        /// <summary>
        /// https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script: the entry of the module map
        /// of the window for <paramref name="key"/>, fetched and parsed in the realm of the window the first time.
        /// </summary>
        private void Load(GlobalScope window, string key, ModuleType type, Element? initiator, Action<ModuleMapEntry> onLoaded, RequestDestination? destination = null)
        {
            var map = _moduleMaps.GetOrCreateValue(window);
            if (map.TryGetValue(key, out var entry))
            {
                if (entry.IsFetching)
                {
                    entry.Waiters.Add(onLoaded);
                }
                else
                {
                    onLoaded(entry);
                }

                return;
            }

            entry = new ModuleMapEntry { IsFetching = true };
            map[key] = entry;
            var url = GetBaseUrl(key);

            // https://html.spec.whatwg.org/multipage/webappapis.html#module-type-allowed: a CSS module is not allowed where
            // CSSStyleSheet is not exposed (in the workers); it is not fetched
            if (type == ModuleType.Css && window is not Window)
            {
                entry.IsFetching = false;
                entry.FetchFailed = true;
                onLoaded(entry);
                return;
            }

            entry.Waiters.Add(onLoaded);
            var fetchOptions = _fetchOptions.GetValueOrDefault(key) ?? new ModuleFetchOptions();
            ModuleFetching.Fetch(window, url, type, fetchOptions, initiator, destinationOverride: destination, referrer: _referrers.GetValueOrDefault(key), isPreload: destination is not null, onComplete: fetched =>
            {
                var realm = Owner.Agent.GetRealm(window);
                using var scope = Owner.EnterJavaScript();
                realm.Run(() =>
                {
                    if (fetched is null)
                    {
                        // A failed fetch is not kept in the module map: the imports that joined it fail, and the next import
                        // fetches the module again (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script)
                        entry.FetchFailed = true;
                        if (map.TryGetValue(key, out var current) && current == entry)
                        {
                            map.Remove(key);
                        }
                    }
                    else
                    {
                        _baseUrls[key] = fetched.Url;
                        _fetchOptions[key] = fetchOptions.WithResponseReferrerPolicy(fetched.ReferrerPolicy);
                        entry.Url = fetched.Url;
                        entry.SourceText = fetched.SourceText;
                        entry.ReferrerPolicy = fetched.ReferrerPolicy;
                        var resolved = new ResolvedSpecifier(new ModuleRequest(key, []), key, Uri: null, SpecifierType.RelativeOrAbsolute);
                        try
                        {
                            entry.Module = Build(Owner.Engine, resolved, fetched.SourceText, type, window);
                        }
                        catch (JavaScriptException ex)
                        {
                            entry.Error = ex.Error;
                        }
                    }

                    entry.IsFetching = false;
                    var waiters = entry.Waiters.ToArray();
                    entry.Waiters.Clear();
                    foreach (var waiter in waiters)
                    {
                        waiter(entry);
                    }

                    return JsValue.Undefined;
                });
            });
        }

        /// <summary>The TypeError of a module that cannot be fetched, recognized by <see cref="IsFetchFailure"/>.</summary>
        private JsValue CreateFetchError(GlobalScope window, string url)
        {
            var error = Owner.Agent.GetRealm(window).CreateError("TypeError", $"Failed to fetch the module '{url}'");
            _fetchFailures.AddOrUpdate(error, error);
            return error;
        }

        /// <summary>The type of a module request (https://html.spec.whatwg.org/multipage/webappapis.html#module-type-from-module-request).</summary>
        private static ModuleType GetModuleType(Engine engine, ModuleRequest request)
        {
            foreach (var attribute in request.Attributes)
            {
                if (attribute.Key != "type")
                    continue;

                return attribute.Value switch
                {
                    "json" => ModuleType.Json,
                    "css" => ModuleType.Css,
                    "text" => ModuleType.Text,
                    "javascript-or-wasm" => throw new JavaScriptException(engine.Intrinsics.TypeError, "The module type 'javascript-or-wasm' cannot be imported explicitly"),
                    _ => throw new JavaScriptException(engine.Intrinsics.TypeError, $"The module type '{attribute.Value}' is not supported"),
                };
            }

            return ModuleType.JavaScript;
        }

        /// <summary>Creates the module record of a JavaScript, JSON, CSS or text module, in the current realm. Throws a SyntaxError for a parse error.</summary>
        private Module Build(Engine engine, ResolvedSpecifier resolved, string sourceText, ModuleType type, GlobalScope window)
        {
            Module module;
            try
            {
                switch (type)
                {
                    case ModuleType.Json:
                        return ModuleFactory.BuildJsonModule(engine, resolved, sourceText);

                    case ModuleType.Css:
                    {
                        // https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-css-module-script: a constructed style sheet whose base URL is the URL of the module
                        var sheet = new CssStyleSheet(window.EnvironmentDocument, new CssStyleSheetInit { BaseUrl = GetBaseUrl(resolved.Key) });
                        sheet.ReplaceSync(sourceText);
                        var value = Owner.Agent.GetRealm(window).ToJs(sheet);
                        return ModuleFactory.BuildJsonModule(engine, value, resolved.Key);
                    }

                    case ModuleType.Text:
                        // https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-text-module-script: CreateTextModule(text)
                        return ModuleFactory.BuildJsonModule(engine, new JsString(sourceText), resolved.Key);

                    default:
                        module = ModuleFactory.BuildSourceTextModule(engine, resolved, sourceText, ModuleParsingOptions.Default with { RetainFunctionSourceText = Owner.RetainFunctionSourceText });
                        break;
                }
            }
            catch (Exception ex) when (ex is Acornima.ParseErrorException or ScriptPreparationException or JavaScriptException)
            {
                // A parse error of the module (Jint wraps it in a JavaScriptException), or invalid JSON. The error is located
                // in the module (the inline scripts are located in their document).
                var error = Owner.Agent.GetRealm(window).CreateError("SyntaxError", ex.Message);
                var (line, column) = ex switch
                {
                    Acornima.ParseErrorException { Error.IsPositionDefined: true } parseError => (parseError.LineNumber, parseError.Column + 1),
                    JavaScriptException { Location.SourceFile: not null } javaScriptException => (javaScriptException.Location.Start.Line, javaScriptException.Location.Start.Column + 1),
                    _ => (0, 0),
                };
                _parseErrorLocations.AddOrUpdate(error, new ErrorLocation(resolved.Key.StartsWith(InlineModulePrefix, StringComparison.Ordinal) ? null : GetBaseUrl(resolved.Key), line, column));
                throw new JavaScriptException(error);
            }
            catch (DomException ex)
            {
                throw new JavaScriptException(Owner.Agent.GetRealm(window).CreateError("TypeError", ex.Message));
            }

            // https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule: an import attribute other than
            // type makes the module fail (before its module specifiers are resolved), as a parse error
            foreach (var request in RequestedModules((CyclicModule)module))
            {
                foreach (var attribute in request.Attributes)
                {
                    if (attribute.Key != "type")
                        throw new JavaScriptException(Owner.Agent.GetRealm(window).CreateError("SyntaxError", $"The import attribute '{attribute.Key}' is not supported"));
                }
            }

            return module;
        }

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_requestedModules")]
        private static safe extern ref HashSet<ModuleRequest> RequestedModules(CyclicModule module);

        /// <summary>The location of a parse error: the URL of the module (null for an inline script), and the position in its source text (1-based, or 0).</summary>
        internal sealed record ErrorLocation(string? Filename, int LineNumber, int ColumnNumber);

        /// <summary>An entry of the module map: a module being fetched, a fetched module (its record, or its parse error), or a fetch failure.</summary>
        internal sealed class ModuleMapEntry
        {
            public bool IsFetching { get; set; }

            /// <summary>The URL of the response.</summary>
            public string Url { get; set; } = "";

            public string SourceText { get; set; } = "";

            /// <summary>The policy of the <c>Referrer-Policy</c> header of the response (the empty string when there is none).</summary>
            public string ReferrerPolicy { get; set; } = "";

            public Module? Module { get; set; }

            public JsValue? Error { get; set; }

            public bool FetchFailed { get; set; }

            public List<Action<ModuleMapEntry>> Waiters { get; } = [];
        }
    }
}
