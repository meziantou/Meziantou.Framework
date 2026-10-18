using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/worklets.html#workletoptions</summary>
[IdlInterface("WorkletOptions", "https://html.spec.whatwg.org/multipage/worklets.html#workletoptions")]
public sealed class WorkletOptions
{
    /// <summary>Gets the credentials mode of the fetches of the module scripts.</summary>
    [IdlMember("credentials", "https://html.spec.whatwg.org/multipage/worklets.html#dom-workletoptions-credentials")]
    public RequestCredentials Credentials { get; init; } = RequestCredentials.SameOrigin;
}

/// <summary>
/// A worklet (https://html.spec.whatwg.org/multipage/worklets.html#worklets-worklet): the module scripts added with
/// <see cref="AddModule"/> run in each of its global scopes (<see cref="WorkletGlobalScope"/>), which have no access to the
/// DOM. A worklet belongs to a document (e.g. <c>CSS.paintWorklet</c>, <see cref="Document.PaintWorklet"/>).
/// </summary>
/// <remarks>
/// The global scopes of a worklet are realms of the script engine of the document
/// (<see cref="BrowsingContextOptions.ScriptEngine"/>): their scripts and their callbacks run on the thread of the event
/// loop of the document, never concurrently with the scripts of the document. The global scopes are created by the first
/// call of <see cref="AddModule"/>, and are terminated when the document is destroyed.
/// </remarks>
[IdlInterface("Worklet", "https://html.spec.whatwg.org/multipage/worklets.html#worklet")]
public abstract class Worklet
{
    private readonly List<WorkletGlobalScope> _globalScopes = [];
    private readonly List<(UrlRecord Url, ModuleFetchOptions Options)> _addedModules = [];
    private readonly Dictionary<string, ModuleResponse> _moduleResponses = new(StringComparer.Ordinal);
    private bool _terminated;

    private protected Worklet(Document document)
    {
        Document = document;
    }

    /// <summary>The document of the worklet: the environment that fetches its module scripts (the outside settings).</summary>
    internal Document Document { get; }

    /// <summary>The destination of the fetches of the module scripts (https://html.spec.whatwg.org/multipage/worklets.html#worklet-destination-type).</summary>
    internal abstract RequestDestination Destination { get; }

    /// <summary>
    /// The number of global scopes created by the first call of <see cref="AddModule"/> (a specification may require several
    /// ones, so that the scripts cannot rely on the state of a global scope).
    /// </summary>
    private protected virtual int GlobalScopeCount => 1;

    /// <summary>The global scopes of the worklet (https://html.spec.whatwg.org/multipage/worklets.html#concept-worklet-global-scopes).</summary>
    internal IReadOnlyList<WorkletGlobalScope> GlobalScopes => _globalScopes;

    /// <summary>Whether the global scopes are terminated (the document was destroyed): no script runs anymore.</summary>
    internal bool IsTerminated => _terminated;

    /// <summary>Creates a global scope of the type of the worklet (https://html.spec.whatwg.org/multipage/worklets.html#worklet-global-scope-type).</summary>
    private protected abstract WorkletGlobalScope CreateGlobalScopeCore();

    /// <summary>
    /// Loads a module script and runs it in the global scopes of the worklet
    /// (https://html.spec.whatwg.org/multipage/worklets.html#dom-worklet-addmodule). The module and its imports are fetched
    /// once for the worklet, with the document as the client of the requests (its content security policies, its referrer
    /// policy, its service worker...), as CORS requests.
    /// </summary>
    /// <param name="moduleUrl">The URL of the module script, relative to the base URL of the document.</param>
    /// <param name="options">The options: the credentials mode of the requests.</param>
    /// <returns>
    /// A task completed when the module ran in every global scope. It fails with a <see cref="DomException"/>: a
    /// <c>SyntaxError</c> when the URL is invalid, an <c>AbortError</c> when a module cannot be fetched (or when the scripts
    /// cannot run: no script engine, a document that is not a secure context or that is destroyed); or with the error of a
    /// module that cannot be parsed or linked.
    /// </returns>
    [IdlMember("addModule", "https://html.spec.whatwg.org/multipage/worklets.html#dom-worklet-addmodule")]
    public Task AddModule(string moduleUrl, WorkletOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(moduleUrl);
        options ??= new WorkletOptions();

        var document = Document;
        if (Reflection.EncodingParseUrl(document, moduleUrl) is not { } moduleUrlRecord)
            return Task.FromException(new DomException($"The URL '{moduleUrl}' is invalid", "SyntaxError"));

        var completion = new TaskCompletionSource();
        if (_terminated || document.RelevantWindow is not { IsSecureContext: true } window || window.ScriptEngine is not { SupportsModuleScripts: true } engine || !window.CanRunScript)
        {
            // Without script engine (or once the document is destroyed), no global scope can run the module; the worklets only
            // exist in the secure contexts
            completion.SetException(new DomException("The worklet cannot run scripts", "AbortError"));
            return completion.Task;
        }

        // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-worklet/module-worker-script-graph: the options of the
        // fetches of the graph
        var fetchOptions = new ModuleFetchOptions
        {
            CredentialsMode = options.Credentials switch
            {
                RequestCredentials.Omit => FetchCredentialsMode.Omit,
                RequestCredentials.Include => FetchCredentialsMode.Include,
                _ => FetchCredentialsMode.SameOrigin,
            },
        };

        // The steps of the specification run in parallel; the global scopes are created from a task, so that the promise is
        // returned before a script of the worklet runs
        document.EnvironmentEventLoop!.QueueTask(() =>
        {
            if (_terminated || !window.CanRunScript)
            {
                completion.TrySetException(new DomException("The worklet cannot run scripts", "AbortError"));
                return;
            }

            if (_globalScopes.Count == 0)
            {
                for (var i = 0; i < GlobalScopeCount; i++)
                {
                    CreateGlobalScope(engine);
                }
            }

            var pendingTasks = _globalScopes.Count;
            var addedSuccessfully = false;
            foreach (var globalScope in _globalScopes.ToArray())
            {
                FetchWorkletScriptGraph(engine, globalScope, moduleUrlRecord, fetchOptions, script =>
                {
                    // The promise is settled by a global task of the document, queued after the tasks that the module script
                    // queued when it ran (e.g. the registration of a paint in the document)
                    if (script is null)
                    {
                        document.QueueGlobalTask(() =>
                        {
                            if (pendingTasks != -1)
                            {
                                pendingTasks = -1;
                                completion.TrySetException(new DomException($"The module '{moduleUrlRecord.Serialize()}' could not be loaded", "AbortError"));
                            }
                        });
                        return;
                    }

                    if (script.ErrorToRethrow is { } error)
                    {
                        document.QueueGlobalTask(() =>
                        {
                            if (pendingTasks != -1)
                            {
                                pendingTasks = -1;
                                completion.TrySetException(CreateErrorForDocument(engine, globalScope, window, error));
                            }
                        });
                        return;
                    }

                    if (!addedSuccessfully)
                    {
                        _addedModules.Add((moduleUrlRecord, fetchOptions));
                        addedSuccessfully = true;
                    }

                    ScriptExecution.RunModuleScript(engine, script);
                    document.QueueGlobalTask(() =>
                    {
                        if (pendingTasks != -1)
                        {
                            pendingTasks--;
                            if (pendingTasks == 0)
                            {
                                completion.TrySetResult();
                            }
                        }
                    });
                });
            }
        });

        return completion.Task;
    }

    /// <summary>
    /// Creates a global scope (https://html.spec.whatwg.org/multipage/worklets.html#create-a-worklet-global-scope): its realm
    /// is created by the script engine, and the modules already added to the worklet run in it.
    /// </summary>
    private protected WorkletGlobalScope CreateGlobalScope(ScriptEngine engine)
    {
        var globalScope = CreateGlobalScopeCore();
        engine.CreateRealm(globalScope);
        foreach (var (url, options) in _addedModules.ToArray())
        {
            FetchWorkletScriptGraph(engine, globalScope, url, options, script =>
            {
                if (script is { ErrorToRethrow: null })
                {
                    ScriptExecution.RunModuleScript(engine, script);
                }
            });
        }

        _globalScopes.Add(globalScope);
        return globalScope;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-worklet-script-graph</summary>
    private static void FetchWorkletScriptGraph(ScriptEngine engine, WorkletGlobalScope globalScope, UrlRecord url, ModuleFetchOptions options, Action<ModuleScript?> onComplete)
    {
        try
        {
            engine.FetchModuleScriptGraph(globalScope, url.Serialize(), options, element: null, script =>
            {
                if (!globalScope.IsTerminated)
                {
                    onComplete(script);
                }
            });
        }
#pragma warning disable CA1031 // An exception of the engine is a failure of the graph
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ScriptErrors.Report(ex, globalScope);
            onComplete(null);
        }
    }

    /// <summary>
    /// The error that rejects the promise of <c>addModule()</c> for the error to rethrow of a script
    /// (https://html.spec.whatwg.org/multipage/worklets.html#dom-worklet-addmodule): the error belongs to the realm of the
    /// worklet, so, as in the browsers, the promise is rejected with its structured clone in the realm of the document (an
    /// error of the same type and message).
    /// </summary>
    private static Exception CreateErrorForDocument(ScriptEngine engine, WorkletGlobalScope globalScope, Window window, object error)
    {
        try
        {
            return new AbortSignalException(engine.StructuredDeserialize(window, engine.StructuredSerializeForAgent(globalScope, error, [])));
        }
#pragma warning disable CA1031 // An error that cannot be cloned is reported as a generic error
        catch (Exception)
#pragma warning restore CA1031
        {
            return new DomException(engine.ExtractErrorInformation(error).Message, "AbortError");
        }
    }

    /// <summary>
    /// The "perform the fetch" hook of the fetches of the worklet (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-worklet-script-graph):
    /// the response of a URL is fetched once for the global scopes of the worklet (the module responses map), so that they
    /// run the same script.
    /// </summary>
    /// <param name="url">The URL of the request.</param>
    /// <param name="fetch">Fetches the response, and gives it to its argument (null for a network error).</param>
    /// <param name="onComplete">Receives the response (null for a network error).</param>
    internal void FetchModuleResponse(string url, Action<Action<DocumentResource?>> fetch, Action<DocumentResource?> onComplete)
    {
        if (_moduleResponses.TryGetValue(url, out var entry))
        {
            if (entry.IsFetching)
            {
                entry.Waiters.Add(onComplete);
            }
            else
            {
                // The cached response is processed from a task, as the response of a fetch
                Document.EnvironmentEventLoop!.QueueTask(() => onComplete(entry.Response));
            }

            return;
        }

        entry = new ModuleResponse { IsFetching = true };
        entry.Waiters.Add(onComplete);
        _moduleResponses[url] = entry;
        fetch(response =>
        {
            entry.Response = response;
            entry.IsFetching = false;
            var waiters = entry.Waiters.ToArray();
            entry.Waiters.Clear();
            foreach (var waiter in waiters)
            {
                waiter(response);
            }
        });
    }

    /// <summary>
    /// Terminates the global scopes (https://html.spec.whatwg.org/multipage/worklets.html#terminate-a-worklet-global-scope),
    /// when the document of the worklet is destroyed.
    /// </summary>
    internal void Terminate()
    {
        if (_terminated)
            return;

        _terminated = true;
        var engine = Document.RelevantWindow?.ScriptEngine;
        foreach (var globalScope in _globalScopes)
        {
            globalScope.IsTerminated = true;
            engine?.DestroyRealm(globalScope);
        }

        _globalScopes.Clear();
        _moduleResponses.Clear();
        OnTerminated();
    }

    /// <summary>Called when the global scopes are terminated.</summary>
    private protected virtual void OnTerminated()
    {
    }

    /// <summary>An entry of the module responses map (https://html.spec.whatwg.org/multipage/worklets.html#concept-worklet-module-responses-map).</summary>
    private sealed class ModuleResponse
    {
        public bool IsFetching { get; set; }

        public DocumentResource? Response { get; set; }

        public List<Action<DocumentResource?>> Waiters { get; } = [];
    }
}
