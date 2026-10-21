using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using static Meziantou.Framework.WebPlatform.JavaScript.JavaScriptValues;

namespace Meziantou.Framework.WebPlatform.JavaScript;

// Module scripts (https://html.spec.whatwg.org/multipage/webappapis.html#module-script) with the module records of the
// engine: the module graphs are loaded asynchronously by JavaScriptModuleLoader, which fetches the modules with the library.
public partial class JavaScriptScriptEngine
{
    internal JavaScriptModuleLoader ModuleLoader { get; }

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
            JsModule module;
            try
            {
                module = ModuleLoader.CreateRootModule(realm, script);
            }
            catch (JsException ex)
            {
                // A parse error of the root is reported when the script runs. The graph completes from a microtask, as when
                // the root is parsed (in order with the inline scripts that have no error)
                script.ErrorToRethrow = ToObject(ex.Value);
                QueueMicrotask(() => onComplete(true));
                return 0;
            }

            LoadDescendants(script, module, onComplete);
            return 0;
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
        ModuleLoader.Load(global, url, ModuleType.JavaScript, options, referrer: null, element, entry =>
        {
            using var scope = EnterJavaScript();
            realm.Run(() =>
            {
                if (entry.FetchFailed)
                {
                    onComplete(null);
                    return 0;
                }

                var script = new ModuleScript(global, entry.SourceText, entry.Url) { Url = entry.Url, Element = element, FetchOptions = options.WithResponseReferrerPolicy(entry.ReferrerPolicy) };
                if (entry.Module is not { } module)
                {
                    script.ErrorToRethrow = entry.Error;
                    onComplete(script);
                    return 0;
                }

                LoadDescendants(script, module, success => onComplete(success ? script : null));
                return 0;
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
        ModuleLoader.Load(global, url, type, options, referrer: null, element, entry => onComplete(!entry.FetchFailed), destination);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#fetch-the-descendants-of-and-link-a-module-script (the loading of the descendants)</summary>
    private void LoadDescendants(ModuleScript script, JsModule module, Action<bool> onComplete)
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

            script.ErrorToRethrow = ToObject(reason);
            onComplete(true);
        });
    }

    /// <inheritdoc/>
    protected internal override void ExecuteModuleScript(ModuleScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var realm = Agent.GetRealm(script.Global);
        using var scope = EnterJavaScript();
        realm.Run(() =>
        {
            if (script.ErrorToRethrow is { } error)
            {
                script.EvaluationState = ModuleEvaluationState.Rejected;
                ReportModuleError(script, ToJs(error), location: null);
                return 0;
            }

            if (script.Record is not JsModule module)
                return 0;

            JsValue evaluation;
            ActiveScripts.Push(module.HostDefined as ScriptHostDefined ?? new ScriptHostDefined { BaseUrl = script.BaseUrl, FetchOptions = script.FetchOptions, IsModule = true });
            try
            {
                module.Link();
                evaluation = module.Evaluate();
            }
            catch (JsException ex)
            {
                script.EvaluationState = ModuleEvaluationState.Rejected;
                ReportModuleError(script, ex.Value, ex.Location);
                return 0;
            }
            finally
            {
                ActiveScripts.Pop();
            }

            // The evaluation promise is settled when the graph has no asynchronous module (a top-level await)
            evaluation.AsObject().TryGetPromiseState(out var state, out _, out _);
            script.EvaluationState = state switch
            {
                JsPromiseState.Pending => ModuleEvaluationState.Pending,
                JsPromiseState.Rejected => ModuleEvaluationState.Rejected,
                _ => ModuleEvaluationState.Fulfilled,
            };

            // The errors of the evaluation (including a top-level await) are reported
            Then(evaluation, onFulfilled: null, onRejected: reason => ReportModuleError(script, reason, module.EvaluationErrorLocation));
            return 0;
        });
    }

    private void ReportModuleError(ModuleScript script, JsValue error, JsSourceLocation? location)
    {
        var information = ExtractErrorInformation(ToObject(error), useStackLocation: false);
        if (information.Filename.Length == 0 && ModuleLoader.GetParseErrorLocation(error) is { } parseErrorLocation)
        {
            // A parse error is located in the module that cannot be parsed (the script itself for an inline script)
            information = new ScriptError(information.Message, information.Error) { Filename = parseErrorLocation.Url ?? script.Url, LineNumber = parseErrorLocation.Line, ColumnNumber = parseErrorLocation.Column };
        }
        else if (information.Filename.Length == 0 && location is { Url: { } url } evaluationLocation)
        {
            // An evaluation error is located where it was thrown
            if (MutedScriptLocations.ContainsKey(url))
            {
                script.Global.ReportException(ScriptError.Muted());
                return;
            }

            information = new ScriptError(information.Message, information.Error) { Filename = url, LineNumber = evaluationLocation.Line, ColumnNumber = evaluationLocation.Column };
        }

        script.Global.ReportException(information.Filename.Length > 0 ? information : new ScriptError(information.Message, information.Error) { Filename = script.Url, LineNumber = information.LineNumber, ColumnNumber = information.ColumnNumber });
    }

    /// <summary>Calls the then method of a promise with .NET callbacks.</summary>
    private void Then(JsValue promise, Action<JsValue>? onFulfilled, Action<JsValue>? onRejected)
    {
        var realm = Engine.CurrentRealm!;
        JsValue fulfilled = onFulfilled is null ? JsValue.Undefined : realm.CreateFunction("", 1, (_, arguments) =>
        {
            onFulfilled(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
            return JsValue.Undefined;
        });
        JsValue rejected = onRejected is null ? JsValue.Undefined : realm.CreateFunction("", 1, (_, arguments) =>
        {
            onRejected(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
            return JsValue.Undefined;
        });

        using var scope = Engine.Enter();
        Engine.Call(promise.AsObject().GetProperty("then"), promise, fulfilled, rejected);
    }

    /// <summary>import.meta (https://html.spec.whatwg.org/multipage/webappapis.html#hostgetimportmetaproperties): url and resolve().</summary>
    internal void InitializeImportMeta(JsSourceTextModule module, JsObject importMeta)
    {
        var baseUrl = (module.HostDefined as ScriptHostDefined)?.BaseUrl ?? module.Url ?? "";
        var window = Agent.GetRealm(module.Realm)?.GlobalScope;
        var resolve = module.Realm.CreateFunction("resolve", 1, (_, arguments) =>
        {
            var specifier = Engine.ToString(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
            if (window is null)
                throw module.Realm.CreateException(JsErrorType.TypeError, "The module has no global object");

            try
            {
                return ModuleSpecifierResolution.Resolve(window, specifier, baseUrl);
            }
            catch (TypeErrorException ex)
            {
                throw module.Realm.CreateException(JsErrorType.TypeError, ex.Message);
            }
        });

        importMeta.DefineProperty("url", JsPropertyDescriptor.Data(baseUrl));
        importMeta.DefineProperty("resolve", JsPropertyDescriptor.Data(resolve));
    }
}

/// <summary>
/// The module loader: the module map of each global object (https://html.spec.whatwg.org/multipage/webappapis.html#module-map),
/// keyed by the URL and the type of the modules. The modules are fetched with <see cref="ModuleFetching"/> (CORS requests,
/// MIME type checks), parsed in the realm of the global object, and shared by the static and dynamic imports.
/// </summary>
internal sealed class JavaScriptModuleLoader(JavaScriptScriptEngine owner) : JsModuleLoader
{
    private readonly ConditionalWeakTable<GlobalScope, Dictionary<(string Url, ModuleType Type), ModuleMapEntry>> _moduleMaps = [];
    private readonly ConditionalWeakTable<JsObject, object> _fetchFailures = [];
    private readonly ConditionalWeakTable<JsObject, ErrorLocation> _parseErrorLocations = [];

    /// <summary>The location of a parse error of a module (see <see cref="ErrorLocation"/>), or null.</summary>
    public ErrorLocation? GetParseErrorLocation(JsValue error) => error.TryGetObject() is { } obj && _parseErrorLocations.TryGetValue(obj, out var location) ? location : null;

    /// <summary>Whether a rejection reason is the error of a module that could not be fetched.</summary>
    public bool IsFetchFailure(JsValue reason) => reason.TryGetObject() is { } obj && _fetchFailures.TryGetValue(obj, out _);

    protected override void InitializeImportMeta(JsSourceTextModule module, JsObject importMeta) => owner.InitializeImportMeta(module, importMeta);

    /// <summary>Parses the root of the graph of an inline module script (it is not added to the module map).</summary>
    public JsModule CreateRootModule(WebIdlRealm<JavaScriptAdapter, object> realm, ModuleScript script)
    {
        var hostDefined = new ScriptHostDefined { BaseUrl = script.BaseUrl, FetchOptions = script.FetchOptions, IsModule = true };
        if (script.IsInline)
        {
            // The positions in an inline script are the positions in its document
            return Build(realm, script.Url, script.SourceText, ModuleType.JavaScript, hostDefined, script.Global, isInline: true, Math.Max(0, script.SourceTextLine - 1), script.SourceTextColumn);
        }

        var module = Build(realm, script.BaseUrl, script.SourceText, ModuleType.JavaScript, hostDefined, script.Global, isInline: false, 0, 0);
        var map = _moduleMaps.GetOrCreateValue(script.Global);
        map.TryAdd((script.BaseUrl, ModuleType.JavaScript), new ModuleMapEntry { Module = module, Url = script.BaseUrl, SourceText = script.SourceText });
        return module;
    }

    /// <summary>HostLoadImportedModule (https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule).</summary>
    protected override void LoadImportedModule(JsModuleLoadRequest request)
    {
        var realm = owner.Agent.GetRealm(request.Realm);
        if (realm is null)
        {
            request.Fail("Module scripts need a global object");
            return;
        }

        var window = realm.GlobalScope;
        JsException? failure = null;
        string? url = null;
        var type = ModuleType.JavaScript;
        ModuleFetchOptions? fetchOptions = null;
        string? referrer = null;
        realm.Run(() =>
        {
            // import() is not allowed in the worklets (the modules of a worklet are the ones added by addModule() and their
            // static imports), nor in the service workers: their scripts are the ones stored when they are installed (the
            // static imports of a module service worker are resolved while its module graph is fetched)
            if (request.IsDynamicImport && window is WorkletGlobalScope)
            {
                failure = request.Realm.CreateException(JsErrorType.TypeError, "import() is not allowed in a worklet");
                return 0;
            }

            if (request.IsDynamicImport && window is ServiceWorkerGlobalScope { IsFetchingModuleScriptGraph: false })
            {
                failure = request.Realm.CreateException(JsErrorType.TypeError, "import() is not allowed in a service worker");
                return 0;
            }

            // An import attribute other than type makes the module fail, as a parse error
            foreach (var attribute in request.Attributes)
            {
                if (attribute.Key != "type")
                {
                    failure = request.Realm.CreateException(JsErrorType.SyntaxError, $"The import attribute '{attribute.Key}' is not supported");
                    return 0;
                }
            }

            // https://html.spec.whatwg.org/multipage/webappapis.html#module-type-from-module-request
            switch (request.Type)
            {
                case null:
                    break;
                case "json":
                    type = ModuleType.Json;
                    break;
                case "css":
                    type = ModuleType.Css;
                    break;
                case "text":
                    type = ModuleType.Text;
                    break;
                case "javascript-or-wasm":
                    failure = request.Realm.CreateException(JsErrorType.TypeError, "The module type 'javascript-or-wasm' cannot be imported explicitly");
                    return 0;
                default:
                    failure = request.Realm.CreateException(JsErrorType.TypeError, $"The module type '{request.Type}' is not supported");
                    return 0;
            }

            // The base URL and the fetch options of the referencing script, or of the document for an event handler (it has
            // no script) or when no script is active
            string baseUrl;
            if (request.ReferrerHostDefined is ScriptHostDefined { BaseUrl: { } referencingBaseUrl } referencingScript)
            {
                baseUrl = referencingBaseUrl;
                referrer = referencingScript.IsGlobalScript ? null : referencingBaseUrl;
                fetchOptions = referencingScript.FetchOptions;
            }
            else
            {
                baseUrl = window.ApiBaseUrl.Serialize();
            }

            try
            {
                url = ModuleSpecifierResolution.Resolve(window, request.Specifier, baseUrl);
            }
            catch (TypeErrorException ex)
            {
                failure = request.Realm.CreateException(JsErrorType.TypeError, ex.Message);
            }

            return 0;
        });

        if (failure is not null)
        {
            request.Fail(failure);
            return;
        }

        Load(window, url!, type, fetchOptions ?? new ModuleFetchOptions(), referrer, initiator: null, entry =>
        {
            if (entry.Module is { } module)
            {
                request.Complete(module);
            }
            else if (entry.FetchFailed)
            {
                // https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule: a new TypeError for each import
                request.Fail(CreateFetchError(window, entry.Url.Length > 0 ? entry.Url : url!));
            }
            else
            {
                request.Fail(ToJs(entry.Error!));
            }
        });
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script: the entry of the module map
    /// of the window for the URL and the type, fetched and parsed in the realm of the window the first time.
    /// <paramref name="onLoaded"/> is called once the entry is fetched (synchronously when it is).
    /// </summary>
    public void Load(GlobalScope window, string url, ModuleType type, ModuleFetchOptions fetchOptions, string? referrer, Element? initiator, Action<ModuleMapEntry> onLoaded, RequestDestination? destination = null)
    {
        var map = _moduleMaps.GetOrCreateValue(window);
        var key = (url, type);
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
        ModuleFetching.Fetch(window, url, type, fetchOptions, initiator, destinationOverride: destination, referrer: referrer, isPreload: destination is not null, onComplete: fetched =>
        {
            var realm = owner.Agent.GetRealm(window);
            using var scope = owner.EnterJavaScript();
            realm.Run(() =>
            {
                if (fetched is null)
                {
                    // A failed fetch is not kept in the module map: the imports that joined it fail, and the next import
                    // fetches the module again (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script)
                    entry.FetchFailed = true;
                    entry.Url = url;
                    if (map.TryGetValue(key, out var current) && current == entry)
                    {
                        map.Remove(key);
                    }
                }
                else
                {
                    entry.Url = fetched.Url;
                    entry.SourceText = fetched.SourceText;
                    entry.ReferrerPolicy = fetched.ReferrerPolicy;
                    var hostDefined = new ScriptHostDefined { BaseUrl = fetched.Url, FetchOptions = fetchOptions.WithResponseReferrerPolicy(fetched.ReferrerPolicy), IsModule = true };
                    try
                    {
                        entry.Module = Build(realm, fetched.Url, fetched.SourceText, type, hostDefined, window, isInline: false, 0, 0);
                    }
                    catch (JsException ex)
                    {
                        entry.Error = ToObject(ex.Value);
                    }
                }

                entry.IsFetching = false;
                var waiters = entry.Waiters.ToArray();
                entry.Waiters.Clear();
                foreach (var waiter in waiters)
                {
                    waiter(entry);
                }

                return 0;
            });
        });
    }

    /// <summary>The TypeError of a module that cannot be fetched, recognized by <see cref="IsFetchFailure"/>.</summary>
    private JsValue CreateFetchError(GlobalScope window, string url)
    {
        var realm = owner.Agent.GetRealm(window);
        var error = (JsObject)realm.Run(() => realm.CreateError("TypeError", $"Failed to fetch the module '{url}'"));
        _fetchFailures.AddOrUpdate(error, error);
        return error;
    }

    /// <summary>Creates the module record of a JavaScript, JSON, CSS or text module, in the realm of the window. Throws a SyntaxError for a parse error.</summary>
    private JsModule Build(WebIdlRealm<JavaScriptAdapter, object> realm, string url, string sourceText, ModuleType type, ScriptHostDefined hostDefined, GlobalScope window, bool isInline, int lineOffset, int columnOffset)
    {
        var engineRealm = (JsRealm)realm.EngineRealm;
        try
        {
            switch (type)
            {
                case ModuleType.Json:
                    return engineRealm.CreateJsonModule(sourceText, url, hostDefined);

                case ModuleType.Css:
                {
                    // https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-css-module-script: a constructed style sheet whose base URL is the URL of the module
                    var sheet = new CssStyleSheet(window.EnvironmentDocument, new CssStyleSheetInit { BaseUrl = url });
                    sheet.ReplaceSync(sourceText);
                    var value = ToJs(realm.ToJs(sheet));
                    return engineRealm.CreateSyntheticModule(["default"], module => module.SetExport("default", value), url, hostDefined);
                }

                case ModuleType.Text:
                    // https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-text-module-script: CreateTextModule(text)
                    return engineRealm.CreateSyntheticModule(["default"], module => module.SetExport("default", sourceText), url, hostDefined);

                default:
                    return engineRealm.ParseModule(sourceText, url, hostDefined, lineOffset, columnOffset);
            }
        }
        catch (JsException ex)
        {
            // A parse error of the module, or invalid JSON: the error is located in the module (the inline scripts are located in their document)
            if (ex.Value.TryGetObject() is { } error)
            {
                _parseErrorLocations.AddOrUpdate(error, new ErrorLocation(isInline ? null : url, ex.Location?.Line ?? 0, ex.Location?.Column ?? 0));
            }

            throw;
        }
        catch (DomException ex)
        {
            throw engineRealm.CreateException(JsErrorType.TypeError, ex.Message);
        }
    }

    /// <summary>The location of a parse error: the URL of the module (null for an inline script), and the position in its source text (1-based, or 0).</summary>
    internal sealed record ErrorLocation(string? Url, int Line, int Column);

    /// <summary>An entry of the module map: a module being fetched, a fetched module (its record, or its parse error), or a fetch failure.</summary>
    internal sealed class ModuleMapEntry
    {
        public bool IsFetching { get; set; }

        /// <summary>The URL of the response.</summary>
        public string Url { get; set; } = "";

        public string SourceText { get; set; } = "";

        /// <summary>The policy of the <c>Referrer-Policy</c> header of the response (the empty string when there is none).</summary>
        public string ReferrerPolicy { get; set; } = "";

        public JsModule? Module { get; set; }

        public object? Error { get; set; }

        public bool FetchFailed { get; set; }

        public List<Action<ModuleMapEntry>> Waiters { get; } = [];
    }
}
